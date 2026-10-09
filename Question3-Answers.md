> **Superseded:** the final, reworked answers (modular monolith + vertical slices) are in [`FinalAnswers/`](FinalAnswers/README.md). This file is kept as history. See `FinalAnswers/CHANGES-AND-REASONS.md` for what changed and why.

# Question 3: Payment Failure Resilience & Integration - Answers

## Context: Scaling Beyond Q2

| Aspect | Q1 | Q2 | Q3 |
|--------|----|----|----|
| Restaurants | 1 | 15 (expecting 50) | 50, expanding to 10 more cities |
| Geography | One city | One city | 3 cities, expanding |
| Developers | 3 | 5 | 12, in 2 teams |
| Driver network | Small | Larger | Much larger |
| Customer base | Small | Larger | Much larger |

This question has two parts: **(A)** a production incident - the payment provider degrades during peak load, and we need to walk through exactly what happens in the architecture, not just the happy path; **(B)** a **final integration challenge** - we've acquired a smaller food-delivery company with its own Customer, Restaurant, Order, and Payment systems, and need to integrate without rewriting either platform.

---

# Part A: The Friday Night Payment Outage

## A.1 Foundation: Payment is Already Extracted

Before walking through the incident, the architecture this scenario runs on:

- **Payment is a standalone service** (extracted from the monolith in Q3, justified by exactly this kind of failure mode - see [Decision 1](#decision-1-extract-payment-before-this-incident-not-during-it))
- Order and Payment communicate asynchronously via RabbitMQ (`ProcessPayment` command, `PaymentSucceeded`/`PaymentFailed`/`PaymentAmbiguous` events)
- Every payment attempt is written to the Payment service's own database **before** calling Stripe, keyed by an idempotency key derived from the order
- A **reconciliation worker** polls Stripe for any attempt left in an unresolved state

### Where Does State Live?

| State | Owner | Why |
|-------|-------|-----|
| **Payment state** (attempt records, provider responses, idempotency keys) | Payment service's own database | Only Payment service talks to Stripe; it's the only system that can be authoritative here |
| **Order state** (lifecycle, what the customer/restaurant sees) | Order module's database (in the monolith) | Order lifecycle is a business concept independent of which payment provider is used |

These are two separate sources of truth, reconciled through events - not a shared table, not a distributed transaction.

### Order States (Extended for Q3)

```
PENDING_ACCEPTANCE → ACCEPTED → PAYMENT_PENDING → PREPARING → READY → OUT_FOR_DELIVERY → DELIVERED → COMPLETED
                  ↘ REJECTED                   ↘ PAYMENT_AMBIGUOUS → (resolves to PREPARING or PAYMENT_FAILED/REFUND_REQUIRED)
                                                ↘ PAYMENT_FAILED
                                                                    ↘ REFUND_REQUIRED → REFUNDED
```

`PAYMENT_AMBIGUOUS` and `REFUND_REQUIRED` are new in Q3 - Q1/Q2 only ever had clean success/fail outcomes. This incident is precisely why they're needed.

---

## A.2 Walkthrough: What Actually Happens

### 1. The payment request times out

Customer places an order → Order service writes the order as `PAYMENT_PENDING` (fast, local DB write, returns immediately to the customer) → publishes `ProcessPayment{orderId, idempotencyKey: "order-{id}-payment", amount}` to RabbitMQ.

Payment service consumes the command, **writes a `payment_attempts` row with state `PENDING` before calling Stripe**, then calls Stripe with a bounded timeout (e.g. 10s) and Stripe's own idempotency key set to the same value.

If the call exceeds the timeout, the HTTP client gives up - but **we don't know whether Stripe actually processed the charge**. We do not mark this `FAILED`. We mark it `AMBIGUOUS` and stop. We do **not** tell the Order service it failed, because telling the customer "payment failed" when it might have succeeded is worse than a short delay.

### 2. The payment actually succeeds despite the timeout

Because the Stripe call was made with Stripe's own idempotency key, Stripe has a definitive, queryable answer for that key regardless of whether our client saw the response. The **reconciliation worker** picks up any `AMBIGUOUS` attempt older than ~30 seconds and queries Stripe directly by idempotency key (`GET /payment_intents?idempotency_key=...`). If Stripe says `succeeded`, the worker updates the local record to `SUCCEEDED` and publishes `PaymentSucceeded` - exactly as if the original call had returned normally, just late. The order transitions `PAYMENT_PENDING → PREPARING` on the same path it always would have.

### 3. The customer retries (clicks "Place Order" again)

The client includes a client-generated order idempotency key with the original "place order" request. If the same key arrives again (double-click, retry after a spinner, etc.), the Order service recognizes it and **returns the existing order** instead of creating a second one. The customer never gets a second charge from their own retry, because no second order - and therefore no second `ProcessPayment` command - is ever created.

### 4. The same payment request is received twice

This is a different case: not the customer retrying, but RabbitMQ redelivering the same `ProcessPayment` message (e.g. the Payment service consumer crashed after processing but before acknowledging the message - at-least-once delivery guarantees a redelivery).

The handler checks for an existing `payment_attempts` row by idempotency key **before** doing anything:
- If one exists and is `SUCCEEDED`/`FAILED`, it just re-publishes the corresponding event and acknowledges - never calls Stripe again.
- If one exists and is `PENDING`/`AMBIGUOUS`, it defers to the reconciliation worker rather than firing a second concurrent charge attempt.

Stripe's own idempotency key is the second line of defense - even if our local check somehow raced, Stripe itself will not double-charge for the same key.

### 5. There's a crash after payment succeeds

Worst case: Stripe confirms success, but the process crashes before the `PaymentSucceeded` event is published. If the payment service only updated its own DB and then crashed before publishing, we'd have a successful charge with no record anywhere that the order should proceed - money taken, food never made.

This is solved with a **transactional outbox**: the Stripe success response, the `payment_attempts` state update, and an outbox row for the `PaymentSucceeded` event are written in **one local database transaction**. A separate relay process reads unpublished outbox rows and publishes them to RabbitMQ, retrying until it succeeds. A crash at any point before the DB commit means nothing happened yet (safe to retry from the top); a crash after the DB commit means the event is durably queued for publishing - it is never lost in memory.

### 6. The payment provider comes back online

The circuit breaker around Stripe calls (tripped `OPEN` once error/timeout rates crossed a threshold during the outage) moves to `HALF-OPEN`, allows a trickle of real requests through, and closes again once they succeed - without a human flipping a switch. Independently, the reconciliation worker keeps sweeping every `AMBIGUOUS`/`PENDING` attempt and resolves it against Stripe's now-healthy API, regardless of whether it was created before or after recovery.

### 7. Failed operations need to be retried

Genuine failures (Stripe definitively says `declined`, not just "timed out") go through bounded retry with exponential backoff **only for retryable error classes** (network errors, 5xx from Stripe) - a hard decline is never blindly retried. After the retry budget is exhausted, the message goes to a dead-letter queue for manual/ops review rather than retrying forever.

### 8. How does the architecture recover, end to end?

- **Real-time path:** circuit breaker contains the blast radius while Stripe is unhealthy; new orders still get created (as `PAYMENT_PENDING`), they just queue up as `AMBIGUOUS`/retrying rather than failing outright.
- **Catch-up path:** the reconciliation worker drains the backlog of `AMBIGUOUS` attempts as Stripe recovers, resolving each to its true outcome.
- **Audit path:** a daily batch job compares our `payment_attempts` table against Stripe's settlement report for the day, flagging any attempt we have no record resolving (belt-and-braces against a bug in the real-time reconciliation).
- **Customer-facing path:** while an order sits in `PAYMENT_AMBIGUOUS`, the customer sees "Confirming your payment..." rather than a hard failure - set expectations without lying about the outcome.

---

## A.3 Questions to Consider - Direct Answers

| Question | Answer |
|----------|--------|
| Where does payment state live? | Payment service's own database - the only system that talks to Stripe |
| Where does order state live? | Order module's database in the monolith |
| What states can an order have? | See state diagram above - including the new `PAYMENT_AMBIGUOUS` and `REFUND_REQUIRED` states |
| What happens if payment succeeds but order creation fails? | The order is always created (as `PAYMENT_PENDING`) **before** payment is attempted, so this specific ordering can't occur. The real equivalent - payment succeeds but the order fails to transition because the event is lost - is prevented by the transactional outbox (A.2.5) |
| How do we prevent duplicate payments? | Three layers: client-side order idempotency key (stops double-clicks), our own idempotency key check before calling Stripe (stops message redelivery), Stripe's own idempotency key (stops any race that slips past the first two) |
| How do we safely retry? | Every handler is idempotent (check-before-act), retries use exponential backoff and are bounded, failed-after-retries goes to a DLQ instead of looping forever |
| Which operations should be synchronous? | Order creation (fast local write), menu/basket reads, anything the customer waits on in the UI |
| Which should be asynchronous? | The actual call to Stripe, notification sending, restaurant order delivery - anything touching an external or slow dependency |
| Where should failures be isolated? | Inside the Payment service, behind a circuit breaker - a Stripe outage degrades payment confirmation latency, not order creation or restaurant browsing |
| How do we know what happened after the fact? | The `payment_attempts` table is a full audit log (every attempt, every state transition, every provider response), plus the transactional outbox gives a durable record of every event we intended to publish |
| How do we reconcile our records with the payment provider? | Real-time reconciliation worker for `AMBIGUOUS` attempts, plus a daily batch reconciliation against Stripe's settlement report |

---

# Part B: Final Integration Challenge - Acquiring a Smaller Platform

## B.1 The Constraint

We've acquired a company with its own Customer, Restaurant, Order, and Payment systems. The brief is explicit: **integrate without rewriting either platform.** This rules out a big-bang migration onto our stack and rules out forcing their systems to adopt our data model directly.

## B.2 Approach: Anti-Corruption Layer, Not a Merge

We introduce one new component - an **Integration Service** - that sits entirely between the two platforms. It is the only thing that talks to the acquired company's APIs, and it is the only thing that translates between their domain model and ours. Neither platform is modified to understand the other's concepts.

```
┌─────────────────────────────┐              ┌─────────────────────────────┐
│       OUR PLATFORM          │              │   ACQUIRED COMPANY PLATFORM │
│  Order / Restaurant /       │              │   Customer / Restaurant /   │
│  Customer / Payment service │              │   Order / Payment           │
└───────────────┬─────────────┘              └───────────────┬─────────────┘
                │  (our canonical domain model only)          │ (their model, untouched)
                ▼                                              ▲
        ┌───────────────────────────────────────────────────────┐
        │              INTEGRATION SERVICE (the ACL)             │
        │  - Exposes OUR domain model to our modules              │
        │  - Internally calls their APIs, translates both ways   │
        │  - Circuit breaker + retries around every call to them │
        │  - Owns the mapping, owns nothing else                 │
        └───────────────────────────────────────────────────────┘
```

### Why an ACL (and not a shared database, and not a rewrite)

| Option | Verdict | Why |
|--------|---------|-----|
| Shared database between the two platforms | Rejected | Couples two independently-evolving schemas; a change on either side breaks the other silently |
| Migrate acquired company onto our platform immediately | Rejected | Explicitly ruled out by the brief; also a large, risky rewrite with no proven need yet |
| Anti-corruption layer / Integration Service | **Chosen** | Keeps both platforms independently deployable and evolvable; isolates translation logic in one place; matches how we already isolate Payment behind an interface |

## B.3 Data Ownership

Each platform remains the **sole owner and source of truth for its own data**. We never dual-write to both databases, and the Integration Service holds no data of its own beyond short-lived caches and a mapping table (e.g. `our_restaurant_id ↔ their_restaurant_id`). If our Order module needs to show a unified order history that includes orders placed against acquired-company restaurants, it stores **our own order record** (so our customer's history is coherent) and the Integration Service is responsible for keeping it in sync with what actually happened on their side - not the other way around.

## B.4 Model Translation

Their Order state machine, their Customer schema, and our equivalents will not line up field-for-field (this is normal after any acquisition, and is exactly the "model translation" concern the brief calls out). The Integration Service's adapters are where that gets reconciled:

```javascript
// Integration Service - Order adapter
function toOurOrder(theirOrder) {
  return {
    orderId: ourIdFor(theirOrder.id),           // mapping table lookup
    state: mapTheirStateToOurs(theirOrder.status), // explicit translation table, not a 1:1 guess
    restaurantId: ourIdFor(theirOrder.restaurant_id),
    items: theirOrder.line_items.map(toOurOrderItem),
    source: 'acquired-platform'                  // never hide where data came from
  };
}
```

We never stretch our domain model to accommodate their quirks, and we never expose their raw model to our system - the adapter is the one place that absorbs the mismatch.

## B.5 Synchronous vs Asynchronous Integration

| Flow | Pattern | Why |
|------|---------|-----|
| Browsing acquired-company restaurants/menus | Synchronous read-through API call, cached (same Redis cache-aside pattern from Q2) | Read-heavy, latency-sensitive, tolerates a short cache TTL |
| An order placed against an acquired-company restaurant | Our Order module creates the order locally (so tracking/history work normally), Integration Service forwards it asynchronously via a command, same as our own Payment pattern | Keeps order placement fast and consistent with how we already treat async side effects |
| Status updates flowing back from their Order/Payment system | Their webhooks translated into our internal event schema by the Integration Service, published to our RabbitMQ | Reuses existing event infrastructure; our Order module only ever consumes our own event shapes |

## B.6 External System Failures

The acquired company's platform is treated exactly like Stripe was in Part A: an external dependency we don't control. The Integration Service wraps every call to it in the same circuit breaker + bounded retry + idempotency pattern used for Payment. If their system is down, browsing their restaurants degrades gracefully (serve cached data, mark them temporarily unavailable) rather than taking down our platform.

## B.7 Versioning and Coupling

- The Integration Service pins to a specific, agreed version of the acquired company's API and we run contract tests against it in CI, so a change on their side fails our build loudly instead of breaking silently in production.
- Our Order/Restaurant/Customer modules depend only on the Integration Service's interface (our canonical model) - they have **zero knowledge** that an acquired company's system exists. This means if we later decide to migrate an acquired restaurant fully onto our platform, we can do it one restaurant at a time behind the same interface, with no changes required in the rest of the system - the Integration Service is also the seam for a future strangler-fig migration, even though nothing about today's integration requires one.

## B.8 What We're Deliberately Not Doing

| Option Rejected | Why |
|-----------------|-----|
| Rewriting the acquired company's platform onto our stack now | Explicitly out of scope; no proven need yet, high risk |
| Direct database access between the two platforms | Breaks data ownership, creates silent coupling |
| Exposing our internal domain model directly to their system (or vice versa) | Defeats the purpose of the ACL; any of their model changes would leak straight into ours |
| A full event-sourced merge of both platforms' histories | Nothing in the brief asks for a unified historical record - only forward integration |

---

## Guiding Principle (Unchanged from Q1/Q2)

**Solve the problem we've actually been given.** Part A's resilience patterns exist because a real, specific failure mode (ambiguous payment outcomes under provider degradation) was described in detail - not as general-purpose "distributed systems best practice." Part B's Integration Service exists because we were explicitly told not to rewrite either platform - it is the minimum architecture that satisfies that constraint, not a speculative platform-unification effort.

---

## Summary

**Part A** - the Friday night incident is handled by treating payment as inherently ambiguous rather than binary: every attempt is recorded before calling Stripe, idempotency keys (ours and Stripe's) prevent duplicate charges across retries and redeliveries, a transactional outbox guarantees a successful payment is never lost to a crash, and a reconciliation worker (plus daily batch audit) resolves anything left uncertain once the provider recovers.

**Part B** - the acquisition is integrated through a single Anti-Corruption Layer (the Integration Service) that owns all translation between our domain model and theirs, isolates failures in the acquired platform exactly like any other external dependency, and keeps both platforms independently deployable - satisfying "don't rewrite either platform" while leaving the door open to a gradual migration later if the business ever wants one.
