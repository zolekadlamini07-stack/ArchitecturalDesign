-- ORDERING schema (Q3). Owned ONLY by the Ordering module.
CREATE SCHEMA IF NOT EXISTS ordering;

CREATE TABLE IF NOT EXISTS ordering.orders (
    id                      uuid PRIMARY KEY,
    customer_id             uuid NOT NULL,
    restaurant_id           uuid NOT NULL,
    status                  text NOT NULL,
    fulfilment              text NOT NULL DEFAULT 'OWN',  -- Q3/challenge: OWN | PARTNER
    delivery_address        text NOT NULL,
    items_total             numeric(10,2) NOT NULL,
    delivery_fee            numeric(10,2) NOT NULL,
    platform_fee            numeric(10,2) NOT NULL,
    total                   numeric(10,2) NOT NULL,
    currency                text NOT NULL,
    rejection_reason        text NULL,
    payment_failure_reason  text NULL,
    created_at              timestamptz NOT NULL
);

-- Q3 DUPLICATE PROTECTION #2 - "ONE PENDING ORDER PER CUSTOMER".
-- A customer who refreshes the page gets a NEW idempotency key, so the key alone can't catch it.
-- This partial unique index can: while an order is PaymentPending, the database refuses a second
-- one for the same customer - even if two API instances race each other.
CREATE UNIQUE INDEX IF NOT EXISTS ux_one_pending_order_per_customer
    ON ordering.orders (customer_id) WHERE status = 'PaymentPending';

-- Q3 DUPLICATE PROTECTION #1 - CLIENT IDEMPOTENCY KEYS.
-- The app sends one Idempotency-Key per checkout. Same key again (double-click, network retry)
-- -> we return the SAME order instead of creating a second one.
CREATE TABLE IF NOT EXISTS ordering.place_order_requests (
    customer_id      uuid NOT NULL,
    idempotency_key  text NOT NULL,
    order_id         uuid NOT NULL REFERENCES ordering.orders(id),
    created_at       timestamptz NOT NULL,
    PRIMARY KEY (customer_id, idempotency_key)
);

CREATE TABLE IF NOT EXISTS ordering.order_lines (
    id            uuid PRIMARY KEY,
    order_id      uuid NOT NULL REFERENCES ordering.orders(id),
    menu_item_id  uuid NOT NULL,
    item_name     text NOT NULL,
    unit_price    numeric(10,2) NOT NULL,
    quantity      int NOT NULL
);

CREATE TABLE IF NOT EXISTS ordering.order_status_history (
    id           uuid PRIMARY KEY,
    order_id     uuid NOT NULL REFERENCES ordering.orders(id),
    from_status  text NULL,
    to_status    text NOT NULL,
    changed_by   text NOT NULL,
    changed_at   timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_orders_restaurant_status ON ordering.orders (restaurant_id, status);
CREATE INDEX IF NOT EXISTS ix_orders_customer_created ON ordering.orders (customer_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_orders_pending_since ON ordering.orders (created_at) WHERE status = 'PaymentPending';

CREATE TABLE IF NOT EXISTS ordering.outbox (
    id            uuid PRIMARY KEY,
    type          text NOT NULL,
    payload       jsonb NOT NULL,
    occurred_at   timestamptz NOT NULL,
    processed_at  timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_ordering_outbox_unprocessed ON ordering.outbox (occurred_at) WHERE processed_at IS NULL;
