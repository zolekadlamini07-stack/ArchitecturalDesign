-- IDENTITY schema. Owned ONLY by the Identity module (D4).
-- Nobody else reads or writes these tables; other modules learn who a user is from the token.
CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE IF NOT EXISTS identity.users (
    id             uuid PRIMARY KEY,
    email          text NOT NULL UNIQUE,
    password_hash  text NOT NULL,
    role           text NOT NULL,          -- Customer | RestaurantStaff | Driver | Admin
    restaurant_id  uuid NULL,              -- set for RestaurantStaff and Driver. An ID only: NO foreign
                                           -- key into the restaurants schema (modules stay separable).
    display_name   text NOT NULL,
    created_at     timestamptz NOT NULL
);

-- Q2: this module PUBLISHES events, so it has its own outbox table (written in the same
-- transaction as the data; the Worker dispatches it). See BuildingBlocks/Outbox.cs.
CREATE TABLE IF NOT EXISTS identity.outbox (
    id            uuid PRIMARY KEY,
    type          text NOT NULL,
    payload       jsonb NOT NULL,
    occurred_at   timestamptz NOT NULL,
    processed_at  timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_identity_outbox_unprocessed ON identity.outbox (occurred_at) WHERE processed_at IS NULL;
