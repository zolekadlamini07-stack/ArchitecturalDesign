-- RESTAURANTS schema (restaurant profile + menu). Owned ONLY by the Restaurants module.
CREATE SCHEMA IF NOT EXISTS restaurants;

CREATE TABLE IF NOT EXISTS restaurants.restaurants (
    id               uuid PRIMARY KEY,
    name             text NOT NULL,
    address          text NOT NULL,
    delivery_fee     numeric(10,2) NOT NULL,
    currency         text NOT NULL,
    is_open          boolean NOT NULL DEFAULT false,
    approval_status  text NOT NULL DEFAULT 'ACTIVE',  -- Q2: PENDING_APPROVAL for self-service applications
    contact_email    text NULL,                       -- Q2: who to invite once approved
    created_at       timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS restaurants.menu_items (
    id             uuid PRIMARY KEY,
    restaurant_id  uuid NOT NULL REFERENCES restaurants.restaurants(id),
    name           text NOT NULL,
    description    text NULL,
    price          numeric(10,2) NOT NULL,
    is_available   boolean NOT NULL DEFAULT true
);

CREATE INDEX IF NOT EXISTS ix_menu_items_restaurant ON restaurants.menu_items (restaurant_id, is_available);
-- Q2 change 4 (measured, not guessed): browse filters by status and sorts by name.
CREATE INDEX IF NOT EXISTS ix_restaurants_browse ON restaurants.restaurants (approval_status, name);

-- Q2: this module PUBLISHES events (RestaurantApproved), so it has its own outbox table.
CREATE TABLE IF NOT EXISTS restaurants.outbox (
    id            uuid PRIMARY KEY,
    type          text NOT NULL,
    payload       jsonb NOT NULL,
    occurred_at   timestamptz NOT NULL,
    processed_at  timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_restaurants_outbox_unprocessed ON restaurants.outbox (occurred_at) WHERE processed_at IS NULL;
