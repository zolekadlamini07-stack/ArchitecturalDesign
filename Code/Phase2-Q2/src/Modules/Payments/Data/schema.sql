-- PAYMENTS schema. Owned ONLY by the Payments module.
-- Q1 keeps it simple: one row per order holding the latest status.
-- (Q3 replaces this with a full attempt ledger + append-only event log.)
CREATE SCHEMA IF NOT EXISTS payments;

CREATE TABLE IF NOT EXISTS payments.payments (
    id                   uuid PRIMARY KEY,
    order_id             uuid NOT NULL UNIQUE,   -- an ID only; no FK into the ordering schema
    amount               numeric(10,2) NOT NULL,
    currency             text NOT NULL,
    status               text NOT NULL,          -- AUTHORISED | DECLINED | FAILED | CAPTURED | VOIDED
    provider_payment_id  text NULL,              -- the provider's reference (e.g. Stripe PaymentIntent id)
    failure_reason       text NULL,
    created_at           timestamptz NOT NULL,
    updated_at           timestamptz NOT NULL
);
