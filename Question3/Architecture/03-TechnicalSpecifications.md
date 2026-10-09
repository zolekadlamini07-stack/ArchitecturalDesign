# Technical Specifications: Q3 Implementation

## Document Purpose

This document provides implementation-level detail for the Q3 changes described in `02-ArchitectureEvolution.md`: the Payment service's schema and code, the circuit breaker and reconciliation worker configuration, the RabbitMQ topology changes, and the Integration Service's adapter shape. It is the Q3 counterpart to Q2's `03-TechnicalSpecifications.md`.

---

## Table of Contents

1. [Infrastructure Setup](#1-infrastructure-setup)
2. [Payment Service Database Schema](#2-payment-service-database-schema)
3. [Idempotency and Payment Processing](#3-idempotency-and-payment-processing)
4. [Circuit Breaker Configuration](#4-circuit-breaker-configuration)
5. [Transactional Outbox Implementation](#5-transactional-outbox-implementation)
6. [Reconciliation Worker](#6-reconciliation-worker)
7. [RabbitMQ Topology Changes](#7-rabbitmq-topology-changes)
8. [Integration Service](#8-integration-service)
9. [Configuration Management](#9-configuration-management)
10. [Monitoring and Observability](#10-monitoring-and-observability)
11. [Deployment Updates](#11-deployment-updates)

---

## 1. Infrastructure Setup

New infrastructure beyond Q2's Redis/RabbitMQ/read-replica stack:

```yaml
# docker-compose.yml (additions for Q3)
services:
  payment-service:
    build: ./services/payment
    environment:
      - DATABASE_URL=postgres://payment_user:${PAYMENT_DB_PASSWORD}@payment-db:5432/payments
      - RABBITMQ_URL=amqp://rabbitmq:5672
      - STRIPE_SECRET_KEY=${STRIPE_SECRET_KEY}
      - CIRCUIT_BREAKER_TIMEOUT_MS=10000
      - CIRCUIT_BREAKER_ERROR_THRESHOLD=50
      - CIRCUIT_BREAKER_RESET_TIMEOUT_MS=30000
    depends_on:
      - payment-db
      - rabbitmq

  payment-db:
    image: postgres:16
    environment:
      - POSTGRES_DB=payments
      - POSTGRES_USER=payment_user
      - POSTGRES_PASSWORD=${PAYMENT_DB_PASSWORD}
    volumes:
      - payment-db-data:/var/lib/postgresql/data

  integration-service:
    build: ./services/integration
    environment:
      - ACQUIRED_PLATFORM_API_URL=${ACQUIRED_PLATFORM_API_URL}
      - ACQUIRED_PLATFORM_API_KEY=${ACQUIRED_PLATFORM_API_KEY}
      - RABBITMQ_URL=amqp://rabbitmq:5672
      - REDIS_URL=redis://redis:6379
    depends_on:
      - rabbitmq
      - redis

volumes:
  payment-db-data:
```

---

## 2. Payment Service Database Schema

```sql
-- Payment attempts: the full audit log and source of truth for payment state
CREATE TABLE payment_attempts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL,
    idempotency_key VARCHAR(255) NOT NULL UNIQUE,
    amount_cents INTEGER NOT NULL,
    currency VARCHAR(3) NOT NULL DEFAULT 'USD',
    state VARCHAR(20) NOT NULL CHECK (state IN ('PENDING', 'AMBIGUOUS', 'SUCCEEDED', 'FAILED')),
    stripe_payment_intent_id VARCHAR(255),
    stripe_response JSONB,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved_at TIMESTAMPTZ
);

CREATE INDEX idx_payment_attempts_order_id ON payment_attempts(order_id);
CREATE INDEX idx_payment_attempts_unresolved
    ON payment_attempts(state, created_at)
    WHERE state IN ('PENDING', 'AMBIGUOUS');

-- Transactional outbox: events waiting to be published
CREATE TABLE outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    aggregate_id UUID NOT NULL,               -- payment_attempts.id
    event_type VARCHAR(50) NOT NULL,          -- PaymentSucceeded | PaymentFailed
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at TIMESTAMPTZ
);

CREATE INDEX idx_outbox_unpublished
    ON outbox_events(created_at)
    WHERE published_at IS NULL;
```

---

## 3. Idempotency and Payment Processing

```javascript
// payment-service/src/handlers/processPayment.js
async function handleProcessPayment(command) {
  const { orderId, idempotencyKey, amountCents } = command;

  // Check for an existing attempt BEFORE calling Stripe
  const existing = await db.query(
    'SELECT * FROM payment_attempts WHERE idempotency_key = $1',
    [idempotencyKey]
  );

  if (existing.rows.length > 0) {
    const attempt = existing.rows[0];
    if (attempt.state === 'SUCCEEDED' || attempt.state === 'FAILED') {
      // Already resolved - just ensure the event was published, don't call Stripe again
      await ensureOutboxEventExists(attempt);
      return;
    }
    // PENDING or AMBIGUOUS - leave it for the reconciliation worker, don't race it
    return;
  }

  // Record intent BEFORE calling Stripe
  const attempt = await db.query(
    `INSERT INTO payment_attempts (order_id, idempotency_key, amount_cents, state)
     VALUES ($1, $2, $3, 'PENDING') RETURNING *`,
    [orderId, idempotencyKey, amountCents]
  );

  try {
    const stripeResult = await circuitBreaker.fire(() =>
      stripe.paymentIntents.create(
        { amount: amountCents, currency: 'usd' },
        { idempotencyKey }   // Stripe's own idempotency protection
      )
    );
    await recordOutcome(attempt.rows[0].id, 'SUCCEEDED', stripeResult);
  } catch (err) {
    if (err.code === 'ETIMEDOUT' || err.isCircuitBreakerOpen) {
      // Genuinely unknown outcome - do NOT mark as FAILED
      await recordOutcome(attempt.rows[0].id, 'AMBIGUOUS', { error: err.message });
    } else {
      // A definitive decline from Stripe is a real failure
      await recordOutcome(attempt.rows[0].id, 'FAILED', { error: err.message });
    }
  }
}
```

---

## 4. Circuit Breaker Configuration

```javascript
// payment-service/src/infrastructure/stripeCircuitBreaker.js
const CircuitBreaker = require('opossum');

const options = {
  timeout: Number(process.env.CIRCUIT_BREAKER_TIMEOUT_MS) || 10000,
  errorThresholdPercentage: Number(process.env.CIRCUIT_BREAKER_ERROR_THRESHOLD) || 50,
  resetTimeout: Number(process.env.CIRCUIT_BREAKER_RESET_TIMEOUT_MS) || 30000,
  rollingCountTimeout: 10000,
  rollingCountBuckets: 10
};

const circuitBreaker = new CircuitBreaker(callStripe, options);

circuitBreaker.on('open', () =>
  logger.warn('Stripe circuit breaker OPEN - failing fast on new payment attempts'));
circuitBreaker.on('halfOpen', () =>
  logger.info('Stripe circuit breaker HALF-OPEN - probing with limited traffic'));
circuitBreaker.on('close', () =>
  logger.info('Stripe circuit breaker CLOSED - Stripe is healthy again'));

module.exports = circuitBreaker;
```

---

## 5. Transactional Outbox Implementation

```javascript
// payment-service/src/handlers/recordOutcome.js
async function recordOutcome(attemptId, state, stripeResponse) {
  await db.transaction(async (tx) => {
    await tx.query(
      `UPDATE payment_attempts
       SET state = $1, stripe_response = $2, resolved_at = now()
       WHERE id = $3`,
      [state, stripeResponse, attemptId]
    );

    if (state === 'SUCCEEDED' || state === 'FAILED') {
      await tx.query(
        `INSERT INTO outbox_events (aggregate_id, event_type, payload)
         VALUES ($1, $2, $3)`,
        [
          attemptId,
          state === 'SUCCEEDED' ? 'PaymentSucceeded' : 'PaymentFailed',
          { attemptId, stripeResponse }
        ]
      );
    }
    // Both writes commit together, or neither does - the outbox row
    // can never be missing for a state change that did happen.
  });
}

// Separate relay process - runs continuously
async function relayOutboxEvents() {
  const unpublished = await db.query(
    'SELECT * FROM outbox_events WHERE published_at IS NULL ORDER BY created_at LIMIT 100'
  );

  for (const event of unpublished.rows) {
    await rabbitmq.publish('payments', event.event_type, event.payload);
    await db.query('UPDATE outbox_events SET published_at = now() WHERE id = $1', [event.id]);
  }
}
```

---

## 6. Reconciliation Worker

```javascript
// payment-service/src/jobs/reconciliationWorker.js
const AMBIGUOUS_THRESHOLD_MS = 30_000;

async function reconcileAmbiguousPayments() {
  const stale = await db.query(
    `SELECT * FROM payment_attempts
     WHERE state IN ('PENDING', 'AMBIGUOUS')
       AND created_at < now() - interval '30 seconds'`
  );

  for (const attempt of stale.rows) {
    const stripeStatus = await stripe.paymentIntents.search({
      query: `metadata['idempotency_key']:'${attempt.idempotency_key}'`
    });

    if (stripeStatus.data.length === 0) {
      continue; // Stripe never received it - leave for retry, don't assume failure
    }

    const outcome = stripeStatus.data[0].status === 'succeeded' ? 'SUCCEEDED' : 'FAILED';
    await recordOutcome(attempt.id, outcome, stripeStatus.data[0]);
  }
}

// Scheduled every 15 seconds
setInterval(reconcileAmbiguousPayments, 15_000);
```

```javascript
// Daily batch reconciliation against Stripe's settlement report - runs via cron
async function dailySettlementReconciliation(date) {
  const settlementReport = await stripe.reporting.reportRuns.create({
    report_type: 'balance.summary.1',
    parameters: { interval_start: date.start, interval_end: date.end }
  });

  const ourRecords = await db.query(
    'SELECT * FROM payment_attempts WHERE created_at BETWEEN $1 AND $2',
    [date.start, date.end]
  );

  const discrepancies = diff(settlementReport, ourRecords.rows);
  if (discrepancies.length > 0) {
    await alerting.notify('payment-reconciliation-discrepancy', discrepancies);
  }
}
```

---

## 7. RabbitMQ Topology Changes

```
Exchange: payments (topic)
  Routing keys:
    payment.process          → consumed by payment-service
    payment.succeeded        → consumed by order-service (monolith)
    payment.failed           → consumed by order-service (monolith)

  Queue: payment-service.process-payment
    Bound to: payment.process
    Dead-letter exchange: payments.dlx (after 3 failed processing attempts)

  Queue: order-service.payment-results
    Bound to: payment.succeeded, payment.failed
```

```javascript
// Order service - unchanged queue consumption pattern from Q2's notification worker,
// just a new queue
channel.consume('order-service.payment-results', async (msg) => {
  const event = JSON.parse(msg.content.toString());
  if (event.type === 'PaymentSucceeded') {
    await orderRepository.updateState(event.orderId, 'PREPARING');
  } else if (event.type === 'PaymentFailed') {
    await orderRepository.updateState(event.orderId, 'PAYMENT_FAILED');
    await notifyCustomer(event.orderId, 'payment_failed');
  }
  channel.ack(msg);
});
```

---

## 8. Integration Service

```javascript
// integration-service/src/adapters/orderAdapter.js
// Anti-corruption layer: translates the acquired platform's Order model to ours
function toOurOrder(theirOrder) {
  return {
    orderId: mappingTable.ourIdFor('order', theirOrder.id),
    state: ORDER_STATE_MAP[theirOrder.status] ?? 'UNKNOWN',
    restaurantId: mappingTable.ourIdFor('restaurant', theirOrder.restaurant_id),
    items: theirOrder.line_items.map(toOurOrderItem),
    source: 'acquired-platform'
  };
}

const ORDER_STATE_MAP = {
  'awaiting_confirmation': 'PENDING_ACCEPTANCE',
  'confirmed': 'ACCEPTED',
  'in_kitchen': 'PREPARING',
  'ready_for_pickup': 'READY',
  'en_route': 'OUT_FOR_DELIVERY',
  'complete': 'DELIVERED'
};

// Circuit breaker around every call to the acquired platform - same pattern as Stripe
const acquiredPlatformBreaker = new CircuitBreaker(callAcquiredPlatformApi, {
  timeout: 8000,
  errorThresholdPercentage: 50,
  resetTimeout: 30000
});

async function getAcquiredRestaurantMenu(restaurantId) {
  const cached = await redis.get(`acquired:menu:${restaurantId}`);
  if (cached) return JSON.parse(cached);

  const theirMenu = await acquiredPlatformBreaker.fire(() =>
    acquiredPlatformClient.getMenu(mappingTable.theirIdFor('restaurant', restaurantId))
  );
  const ourMenu = theirMenu.items.map(toOurMenuItem);
  await redis.set(`acquired:menu:${restaurantId}`, JSON.stringify(ourMenu), 'EX', 300);
  return ourMenu;
}
```

---

## 9. Configuration Management

```bash
# .env.example (additions for Q3)

# Payment Service
PAYMENT_DB_PASSWORD=
STRIPE_SECRET_KEY=
CIRCUIT_BREAKER_TIMEOUT_MS=10000
CIRCUIT_BREAKER_ERROR_THRESHOLD=50
CIRCUIT_BREAKER_RESET_TIMEOUT_MS=30000

# Integration Service
ACQUIRED_PLATFORM_API_URL=
ACQUIRED_PLATFORM_API_KEY=
ACQUIRED_PLATFORM_API_VERSION=2026-01-01   # pinned, checked by contract tests in CI
```

---

## 10. Monitoring and Observability

| Metric | Purpose | Alert Threshold |
|--------|---------|------------------|
| `payment_attempts.state=AMBIGUOUS` (count, age) | Surfaces unresolved payments before customers complain | Any record older than 5 minutes |
| Circuit breaker state changes (open/half-open/close) | Tracks Stripe health in real time | Alert on `open` |
| Outbox relay lag (unpublished rows, oldest age) | Confirms the outbox isn't backing up | Oldest unpublished row > 60s |
| Daily reconciliation discrepancy count | Catches anything the real-time path missed | Any discrepancy > 0 |
| Integration Service circuit breaker state | Tracks acquired-platform health | Alert on `open` |

---

## 11. Deployment Updates

```yaml
# Two new independently deployable services, each with their own pipeline
# .github/workflows/deploy-payment-service.yml
# .github/workflows/deploy-integration-service.yml
#
# The monolith's existing pipeline is unchanged - it no longer contains
# Payment's code, so Payment deploys do not trigger a full monolith redeploy.
```

Team Platform owns and can independently release the Payment service and the Integration Service. Team Commerce's monolith deploys are unaffected by either - exactly the independence the team-realignment decision (Change 6 in `02-ArchitectureEvolution.md`) was meant to produce.
