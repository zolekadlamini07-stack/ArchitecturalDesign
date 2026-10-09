-- DELIVERY schema. Owned ONLY by the Delivery module.
CREATE SCHEMA IF NOT EXISTS delivery;

-- D7: every driver belongs to exactly one restaurant.
CREATE TABLE IF NOT EXISTS delivery.drivers (
    id             uuid PRIMARY KEY,         -- same id as the driver's Identity user
    restaurant_id  uuid NOT NULL,            -- ID only, no cross-schema FK
    display_name   text NOT NULL,
    created_at     timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS delivery.driver_shifts (
    id          uuid PRIMARY KEY,
    driver_id   uuid NOT NULL REFERENCES delivery.drivers(id),
    started_at  timestamptz NOT NULL,
    ended_at    timestamptz NULL            -- NULL = currently on shift ("available for deliveries")
);

CREATE TABLE IF NOT EXISTS delivery.deliveries (
    id                uuid PRIMARY KEY,
    order_id          uuid NOT NULL UNIQUE,  -- one delivery per order
    restaurant_id     uuid NOT NULL,
    driver_id         uuid NULL REFERENCES delivery.drivers(id), -- NULL until a driver accepts
    status            text NOT NULL,         -- REQUESTED | ACCEPTED | PICKED_UP | DELIVERED
    delivery_address  text NOT NULL,         -- SNAPSHOT copied from the order
    requested_at      timestamptz NOT NULL,
    accepted_at       timestamptz NULL,
    picked_up_at      timestamptz NULL,
    delivered_at      timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_deliveries_restaurant_status ON delivery.deliveries (restaurant_id, status);
