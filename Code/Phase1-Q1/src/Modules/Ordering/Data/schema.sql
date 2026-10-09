-- ORDERING schema. Owned ONLY by the Ordering module.
CREATE SCHEMA IF NOT EXISTS ordering;

CREATE TABLE IF NOT EXISTS ordering.orders (
    id                uuid PRIMARY KEY,
    customer_id       uuid NOT NULL,    -- IDs only: no foreign keys into customers/restaurants schemas,
    restaurant_id     uuid NOT NULL,    -- so these modules can be separated later without a rewrite
    status            text NOT NULL,
    delivery_address  text NOT NULL,    -- snapshot
    items_total       numeric(10,2) NOT NULL,
    delivery_fee      numeric(10,2) NOT NULL,
    platform_fee      numeric(10,2) NOT NULL,
    total             numeric(10,2) NOT NULL,
    currency          text NOT NULL,
    rejection_reason  text NULL,
    created_at        timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ordering.order_lines (
    id            uuid PRIMARY KEY,
    order_id      uuid NOT NULL REFERENCES ordering.orders(id),
    menu_item_id  uuid NOT NULL,
    item_name     text NOT NULL,          -- snapshot (like a receipt)
    unit_price    numeric(10,2) NOT NULL, -- snapshot
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

-- The restaurant dashboard asks "my orders in this status" constantly.
CREATE INDEX IF NOT EXISTS ix_orders_restaurant_status ON ordering.orders (restaurant_id, status);
CREATE INDEX IF NOT EXISTS ix_orders_customer_created ON ordering.orders (customer_id, created_at DESC);
