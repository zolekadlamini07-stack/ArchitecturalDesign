-- NOTIFICATIONS schema. Owned ONLY by the Notifications module.
-- A log of what was sent and when - support uses it to answer "was I told?".
CREATE SCHEMA IF NOT EXISTS notifications;

CREATE TABLE IF NOT EXISTS notifications.notification_log (
    id         uuid PRIMARY KEY,
    recipient  text NOT NULL,
    subject    text NOT NULL,
    message    text NOT NULL,
    sent_at    timestamptz NOT NULL
);
