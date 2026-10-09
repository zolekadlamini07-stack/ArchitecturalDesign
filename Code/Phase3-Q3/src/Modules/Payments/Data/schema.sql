-- =============================================================================================
-- PAYMENTS schema (Q3). Owned ONLY by the Payments module.
-- Q1/Q2 had one 'payments' row per order with the latest status. Q3 replaces it with a LEDGER.
-- =============================================================================================
CREATE SCHEMA IF NOT EXISTS payments;

-- One row per attempt to authorise an order. The row is written BEFORE we call the provider,
-- so we always know what we ASKED, even if we never hear back.
CREATE TABLE IF NOT EXISTS payments.payment_attempts (
    id                   uuid PRIMARY KEY,    -- ALSO the idempotency key sent to the provider
    order_id             uuid NOT NULL,
    amount               numeric(10,2) NOT NULL,
    currency             text NOT NULL,
    payment_token        text NOT NULL,       -- provider token, NOT a card number
    status               text NOT NULL,       -- REQUESTED | IN_FLIGHT | AUTHORISED | DECLINED | UNKNOWN | CAPTURED | VOIDED
    provider_payment_id  text NULL,
    created_at           timestamptz NOT NULL,
    updated_at           timestamptz NOT NULL
);
-- DUPLICATE PROTECTION: at most ONE live attempt per order. A redelivered OrderPlaced job cannot
-- create a second attempt - the database refuses. (A DECLINED attempt doesn't count: the customer
-- may try another card.)
CREATE UNIQUE INDEX IF NOT EXISTS ux_one_live_attempt_per_order
    ON payments.payment_attempts (order_id) WHERE status <> 'DECLINED';
CREATE INDEX IF NOT EXISTS ix_attempts_needing_reconciliation
    ON payments.payment_attempts (updated_at) WHERE status IN ('UNKNOWN', 'IN_FLIGHT');

-- APPEND-ONLY audit log: every request, response, timeout, webhook and reconciliation result.
-- Never updated, never deleted. Support and finance can replay exactly what happened.
CREATE TABLE IF NOT EXISTS payments.payment_events (
    id          uuid PRIMARY KEY,
    attempt_id  uuid NOT NULL,
    order_id    uuid NOT NULL,
    what        text NOT NULL,
    detail      text NULL,
    at          timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_payment_events_order ON payments.payment_events (order_id, at);

-- INBOX for provider webhooks: the provider's event id is the PRIMARY KEY, so the same webhook
-- delivered twice is stored once and processed once.
CREATE TABLE IF NOT EXISTS payments.provider_webhook_inbox (
    provider_event_id  text PRIMARY KEY,
    reference          text NOT NULL,     -- our attempt id, echoed back by the provider
    status             text NOT NULL,
    received_at        timestamptz NOT NULL,
    processed_at       timestamptz NULL
);

-- Findings of the daily settlement check that need a human.
CREATE TABLE IF NOT EXISTS payments.settlement_exceptions (
    id          uuid PRIMARY KEY,
    attempt_id  uuid NULL,
    reference   text NOT NULL,
    problem     text NOT NULL,
    found_at    timestamptz NOT NULL
);

-- Q3: Payments now PUBLISHES events (PaymentAuthorised / PaymentDeclined) through an outbox.
CREATE TABLE IF NOT EXISTS payments.outbox (
    id            uuid PRIMARY KEY,
    type          text NOT NULL,
    payload       jsonb NOT NULL,
    occurred_at   timestamptz NOT NULL,
    processed_at  timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_payments_outbox_unprocessed ON payments.outbox (occurred_at) WHERE processed_at IS NULL;

-- ---------------------------------------------------------------------------------------------
-- DEVELOPMENT ONLY: "pretend this is Stripe's database". The fake provider keeps its own records
-- here so that same-key replays, lookups and settlement reports behave like a real provider's,
-- even across Worker restarts. Nothing in the Payments module reads this except the fake adapter.
-- ---------------------------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS fake_provider;
CREATE TABLE IF NOT EXISTS fake_provider.charges (
    idempotency_key      text PRIMARY KEY,
    provider_payment_id  text NOT NULL,
    reference            text NOT NULL,
    amount_minor         bigint NOT NULL,
    status               text NOT NULL,     -- authorised | declined | captured | voided
    created_at           timestamptz NOT NULL
);
