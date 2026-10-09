-- CUSTOMERS schema. Owned ONLY by the Customers module.
CREATE SCHEMA IF NOT EXISTS customers;

CREATE TABLE IF NOT EXISTS customers.customer_profiles (
    customer_id   uuid PRIMARY KEY,   -- same id as the Identity user (an ID only, no cross-schema foreign key)
    display_name  text NOT NULL,
    phone         text NULL,
    created_at    timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS customers.addresses (
    id           uuid PRIMARY KEY,
    customer_id  uuid NOT NULL REFERENCES customers.customer_profiles(customer_id), -- FK INSIDE the module is fine
    line1        text NOT NULL,
    city         text NOT NULL,
    postal_code  text NOT NULL,
    is_default   boolean NOT NULL DEFAULT false
);
