-- RESTAURANTS schema (restaurant profile + menu). Owned ONLY by the Restaurants module.
CREATE SCHEMA IF NOT EXISTS restaurants;

CREATE TABLE IF NOT EXISTS restaurants.restaurants (
    id            uuid PRIMARY KEY,
    name          text NOT NULL,
    address       text NOT NULL,
    delivery_fee  numeric(10,2) NOT NULL,
    currency      text NOT NULL,
    is_open       boolean NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS restaurants.menu_items (
    id             uuid PRIMARY KEY,
    restaurant_id  uuid NOT NULL REFERENCES restaurants.restaurants(id),
    name           text NOT NULL,
    description    text NULL,
    price          numeric(10,2) NOT NULL,
    is_available   boolean NOT NULL DEFAULT true
);

-- GetMenu reads "all items for one restaurant" constantly.
CREATE INDEX IF NOT EXISTS ix_menu_items_restaurant ON restaurants.menu_items (restaurant_id);
