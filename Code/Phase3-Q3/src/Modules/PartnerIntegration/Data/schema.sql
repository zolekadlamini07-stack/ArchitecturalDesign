-- PARTNER_INTEGRATION schema (challenge). Owned ONLY by the PartnerIntegration module.
-- It owns the MAPPING between the two companies - nothing else. Each platform still owns its data.
CREATE SCHEMA IF NOT EXISTS partner_integration;

-- ID MAPPING: their ids never leave this module. Our modules only ever see OUR ids.
CREATE TABLE IF NOT EXISTS partner_integration.id_map (
    entity_type  text NOT NULL,     -- 'restaurant' | 'menu_item'
    their_id     text NOT NULL,
    our_id       uuid NOT NULL,
    PRIMARY KEY (entity_type, their_id),
    UNIQUE (entity_type, our_id)
);

-- Orders we forwarded to their Order system (external_ref on their side = our order id).
CREATE TABLE IF NOT EXISTS partner_integration.forwarded_orders (
    our_order_id    uuid PRIMARY KEY,
    their_order_no  text NOT NULL,
    their_status    text NOT NULL,
    forwarded_at    timestamptz NOT NULL,
    confirmed_at    timestamptz NULL,
    timed_out       boolean NOT NULL DEFAULT false
);

-- INBOX for their webhooks: duplicates dropped by primary key; out-of-order updates are ignored by
-- Ordering's state machine (it refuses to go backwards).
CREATE TABLE IF NOT EXISTS partner_integration.partner_inbox (
    event_id        text PRIMARY KEY,
    their_order_no  text NOT NULL,
    their_status    text NOT NULL,
    reason          text NULL,
    received_at     timestamptz NOT NULL,
    processed_at    timestamptz NULL,
    error           text NULL           -- e.g. an UNKNOWN status: an alert, never a guess
);

-- Last successful catalogue sync - if it gets too old, their restaurants are shown unavailable.
CREATE TABLE IF NOT EXISTS partner_integration.sync_state (
    name             text PRIMARY KEY,
    last_success_at  timestamptz NOT NULL,
    last_snapshot    jsonb NOT NULL      -- last-known-good copy of their catalogue (their model)
);

-- DEVELOPMENT ONLY: "pretend this is the acquired company's own Order database".
CREATE SCHEMA IF NOT EXISTS fake_partner;
CREATE TABLE IF NOT EXISTS fake_partner.orders (
    order_no      text PRIMARY KEY,
    external_ref  text NOT NULL UNIQUE,  -- OUR order id: makes forwarding idempotent
    store_id      int NOT NULL,
    status        text NOT NULL,
    created_at    timestamptz NOT NULL
);
