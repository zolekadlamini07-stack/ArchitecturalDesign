# Question 3 - Research Notes (concepts behind the answer)

> Deeper background from earlier work: `../../Question3/Research/01-ResiliencePatterns.md`, `02-IdempotencyAndConsistency.md`, `03-ReconciliationStrategies.md`.
> **One difference:** that earlier work **extracted Payments into its own service** and used RabbitMQ. The final answer keeps Payments **as a module** and uses the Q2 PostgreSQL queue. The patterns (idempotency, outbox, circuit breaker, reconciliation) are identical; only *where they run* differs. See `../CHANGES-AND-REASONS.md` C7.

---

## 1. Why "unknown" is a real outcome

When you send a request over a network and get no reply, any of these may have happened:
1. The request never arrived.
2. It arrived, was processed, and the **reply** was lost.
3. It's still being processed.

You **cannot** tell these apart from the caller's side (this is the classic *Two Generals problem*). So a timeout means **"unknown"**, never "failed". The only safe responses are: ask again **in a way that can't do it twice** (idempotency), or **look it up** later (reconciliation).

---

## 2. Idempotency keys

**Idea:** the caller attaches a unique key to an operation. The receiver remembers keys it has seen and, for a repeat, **returns the stored result instead of redoing the work**.

```
POST /v1/payment_intents
Idempotency-Key: 7f3c...-A          <- our attempt id
-> first time: creates & authorises, stores result under key
-> repeat with same key: returns the SAME result, no new charge
```
- **Stripe** stores keys for at least 24 h. A repeat with the same key but *different parameters* is rejected, so **never reuse a key for a different amount/card**. Most providers (Adyen, Braintree, PayPal, ...) have an equivalent.
- **We apply the same idea ourselves** at `POST /orders` (client-generated key) and in every job handler (unique constraints / "check state first").
- **Where the key comes from matters:** the key must be created **before** the first attempt and **stored**, so a retry after a crash can reuse it. That's why the attempt row (whose id *is* the key) is written before calling the provider.

---

## 3. Timeouts, retries, backoff, jitter

- **Timeout:** every remote call has one (we use 10 s for authorisation). Without it, one slow dependency holds threads and connections forever.
- **Retry only idempotent operations, only for transient errors** (timeouts, connection resets, 5xx, 429). Never retry business answers (declines).
- **Exponential backoff:** wait 30 s, 1 m, 2 m, 5 m, ... between tries, so a struggling dependency gets breathing room.
- **Jitter:** add randomness to each wait, so a thousand retries don't all fire in the same second (a thundering herd).
- **Bounded:** after N attempts, stop and dead-letter. Infinite retries hide bugs and burn money.

---

## 4. Circuit breaker

Michael Nygard, *Release It!*. A wrapper around calls to a dependency that tracks failures:

```
        failures exceed threshold
 CLOSED ---------------------------> OPEN  (calls fail fast, no network call)
   ^                                   |
   | trial calls succeed               | cool-down elapses
   |                                   v
   +-------------------------------- HALF-OPEN (let a few trial calls through)
                trial call fails -> back to OPEN
```
- **Why:** stops wasting time (10 s timeouts) and stops adding load to something that's already down, and the system recovers automatically.
- **Libraries:** opossum (Node), Polly (.NET), Resilience4j (Java).
- **In our design:** when OPEN, payment jobs are *rescheduled*, not failed, so orders just stay `PAYMENT_PENDING` a bit longer.

---

## 5. Bulkhead

Named after a ship's watertight compartments: isolate resources so one flooded part doesn't sink the ship. We do it with **separate worker queues, each with its own concurrency limit** (`payments`, `notifications`, `integration`, `reporting`), and by keeping **provider calls out of the API process entirely**. A stuck provider can occupy at most the `payments` slots.

---

## 6. Transactional outbox (recap from Q2) + inbox

- **Outbox:** write the state change **and** the event in one local transaction. A dispatcher publishes events from the table. This guarantees *"if the status changed, the event will be delivered (at least once)"*.
- **Inbox:** on the receiving side, record processed message ids (or rely on unique constraints/state checks), so a redelivery has no extra effect. For provider webhooks: store each webhook with the provider's event id as a unique key **before** processing, and reply 200 fast.
- Together they give **effectively-once processing**: at-least-once delivery + idempotent receivers.

---

## 7. Reconciliation: three layers

| Layer | Speed | Catches |
|-------|-------|---------|
| **Webhooks** (provider pushes events to us) | Seconds | Most late outcomes. **Not guaranteed:** webhooks can be delayed or lost, and we can be down. |
| **Sweeper / polling** (we ask the provider about stuck attempts) | Minutes | Anything webhooks missed. We control the timing. |
| **Settlement reconciliation** (compare the provider's daily report with our ledger) | Daily | Bugs in the other two. Required by finance anyway. |

Rule of thumb from payments engineering: **never rely on a single channel for the truth about money.**

---

## 8. Sagas and compensation

A **saga** is a business process spread over steps that can't share one transaction. Each step has a **compensating action** that semantically undoes it.

| Step | Compensation |
|------|--------------|
| Authorise payment | Void authorisation |
| Capture payment | Refund |
| Send order to restaurant | Cancel order + notify restaurant |

Our flow is a small **choreographed saga** (modules react to each other's events) built from outbox + idempotent handlers. No saga framework or orchestrator is needed at this size.

---

## 9. Order-first vs payment-first

| Approach | Risk |
|----------|------|
| Payment first, then create order | Payment succeeds, order creation crashes → **charged with no order** (the exact Friday complaint). |
| **Order first (`PAYMENT_PENDING`), then payment** (chosen) | Worst case: an order with no payment, which we can see, resolve and expire. Nobody is charged for something we have no record of. |

---

## 10. Observability (knowing what happened)

- **Correlation id:** use the order id in every log line, job and provider metadata field, so one search shows the whole story.
- **Structured logs** (JSON) + **metrics** + **alerts** from the hosting platform or a hosted tool (e.g. Grafana Cloud, Datadog, Sentry for errors).
- **Key alerts for this incident:** circuit breaker OPEN; count of `UNKNOWN` attempts; age of the oldest `PAYMENT_PENDING` order; dead-letter count; provider latency p95.
- **Audit log:** `payment_events` is append-only, so support and finance can replay exactly what happened.

---

## 11. Glossary

| Term | One line |
|------|----------|
| Ambiguous / unknown outcome | We sent a request and can't know whether it took effect. |
| Idempotency key | Unique key making a repeat request return the original result. |
| Circuit breaker | Stops calling a failing dependency, retries after a cool-down. |
| Bulkhead | Separate resource pools so one failure can't consume everything. |
| Backoff + jitter | Increasing, randomised waits between retries. |
| Inbox | Record of processed incoming messages for de-duplication. |
| Reconciliation | Comparing our records with the external system's and fixing differences. |
| Compensation | An action that undoes a previous step (void, refund). |
| Saga | Multi-step process using compensations instead of one big transaction. |
| Dead-letter | Holding area for messages that keep failing. |
