-- NOTIFICATIONS schema. Owned ONLY by the Notifications module.
CREATE SCHEMA IF NOT EXISTS notifications;

CREATE TABLE IF NOT EXISTS notifications.notification_log (
    id         uuid PRIMARY KEY,
    event_id   uuid NOT NULL,      -- Q2: which event caused this notification
    recipient  text NOT NULL,
    subject    text NOT NULL,
    message    text NOT NULL,
    sent_at    timestamptz NOT NULL,
    -- Q2 IDEMPOTENCY: jobs can run twice (at-least-once delivery). This unique key makes the
    -- second attempt to log the same notification fail, so the handler knows "already sent".
    CONSTRAINT uq_notification_once UNIQUE (event_id, recipient)
);
