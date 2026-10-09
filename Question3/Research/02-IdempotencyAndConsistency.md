# Idempotency and Consistency Research

## Document Purpose

This document researches how to make retried operations safe (idempotency) and how to guarantee a successful payment is never lost to a crash (the dual-write problem and the transactional outbox pattern). These are the two pieces of research behind Changes 2 and 4 in `../Architecture/02-ArchitectureEvolution.md`.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Idempotency Fundamentals](#2-idempotency-fundamentals)
3. [Pattern: Idempotency Keys](#3-pattern-idempotency-keys)
4. [The Dual-Write Problem](#4-the-dual-write-problem)
5. [Pattern: Transactional Outbox](#5-pattern-transactional-outbox)
6. [Alternative Considered: Change Data Capture](#6-alternative-considered-change-data-capture)
7. [Alternative Considered: Two-Phase Commit](#7-alternative-considered-two-phase-commit)
8. [Trade-offs Analysis](#8-trade-offs-analysis)
9. [Application to Q3](#9-application-to-q3)
10. [References](#10-references)

---

## 1. The Problem We're Solving

Two distinct but related failure modes appear in the Friday-night incident:

1. **Duplicate charges** - a customer double-clicking "Place Order," or RabbitMQ redelivering a message after a consumer crash, can cause the same logical payment to be attempted twice.
2. **Lost successes** - if the Payment service crashes after Stripe confirms a charge but before that fact is durably communicated to the rest of the system, the charge exists with no record anywhere that the order should proceed.

These require different techniques: idempotency for the first, the transactional outbox for the second.

---

## 2. Idempotency Fundamentals

An operation is **idempotent** if performing it multiple times has the same effect as performing it once. HTTP's GET, PUT, and DELETE are specified as idempotent by design; POST (which "place order" and "charge payment" both are) is not, by default - calling it twice ordinarily does the thing twice.

Payments are a textbook case where this default is dangerous: a POST that creates a charge, called twice, creates two charges.

---

## 3. Pattern: Idempotency Keys

**What it is:** The caller generates (or derives) a unique key for a logical operation and attaches it to every attempt, including retries. The receiver checks whether it has already processed that key before doing the work again.

**The Critical Design Choice - Deterministic, Not Random, Keys:** If a fresh random key were generated for every retry, the receiver would have no way to recognize "this is the same logical request as before" - the protection would be defeated entirely. Our key is derived deterministically from the order: `order-{orderId}-payment`. Every retry of the same order's payment - whether customer-driven or message-redelivery-driven - produces the exact same key.

**Two Independent Layers:**
1. Our own check: before calling Stripe, the Payment service looks for an existing `payment_attempts` row with that idempotency key.
2. Stripe's own idempotency key support: we pass the same key to Stripe's API, so even in a race condition our own check somehow misses, Stripe itself will not process the charge twice for the same key. Stripe's documentation explicitly recommends this as the mechanism for safe retries.

---

## 4. The Dual-Write Problem

**What it is:** When an operation needs to update two separate systems (here: our database, and the message broker) and there's no way to make both updates happen atomically, a crash between the two can leave them inconsistent - one updated, the other not.

```
Write to DB  ──────▶  [CRASH HERE]  ──────▶  Publish to broker

If the crash happens here, the DB says "payment succeeded"
but nothing downstream ever finds out.
```

This is a well-documented trap in distributed systems - Martin Kleppmann's *Designing Data-Intensive Applications* discusses it at length, and it's commonly just called "the dual-write problem" in microservices literature.

---

## 5. Pattern: Transactional Outbox

**What it is:** Instead of writing to the database and then separately publishing to the broker, the event to be published is written as a row in an "outbox" table, in the **same local database transaction** as the business state change. A separate relay process reads unpublished outbox rows and publishes them, retrying until each succeeds, then marks them published.

**Why It Works:** Because the state change and the outbox row are one atomic transaction, there is no window where one happened and the other didn't - either both happened (and the event will eventually be published, even if the relay has to retry) or neither did (safe to retry the whole operation from scratch).

**Origin:** Documented extensively by Chris Richardson on microservices.io as one of the standard patterns for achieving reliable messaging from a service with its own database - directly applicable to our Payment service, which already has its own PostgreSQL instance after Change 1.

---

## 6. Alternative Considered: Change Data Capture

**What it is:** Instead of an application-level outbox table, a tool (commonly Debezium) tails the database's write-ahead log directly and publishes changes as they're committed - no outbox table needed, because the database's own transaction log is the source of truth for "what changed."

**Why Not Chosen Now:** CDC removes the need for a relay process polling an outbox table, but it requires operating a CDC pipeline (Debezium plus typically Kafka Connect) that is not otherwise part of our stack. For a single service (Payment) with one event type to publish reliably, the simpler application-level outbox avoids introducing new infrastructure. This is a reasonable option to revisit if more services need the same guarantee at once.

---

## 7. Alternative Considered: Two-Phase Commit

**What it is:** A distributed transaction protocol (2PC) in which a coordinator asks all participants (here: our database and Stripe) to "prepare," waits for all to confirm, then tells them all to "commit" - or aborts all if any participant can't prepare.

**Why Not Chosen:** Stripe does not participate in a two-phase commit protocol - it's an HTTP API, not a resource manager we can enlist in a distributed transaction. Even where 2PC is technically available (e.g., between two databases we control), it's widely discouraged for its availability cost: all participants must be reachable and responsive for the duration of the transaction, which is exactly the kind of fragility an external-provider outage makes worse, not better. Pat Helland's widely-cited paper "Life Beyond Distributed Transactions" makes this argument in depth.

---

## 8. Trade-offs Analysis

| Approach | Prevents Duplicate Charges | Prevents Lost Successes | New Infrastructure | Complexity |
|----------|------------------------------|---------------------------|----------------------|------------|
| No idempotency, direct publish | No | No | None | Lowest |
| Idempotency keys only | Yes | No | None | Low |
| Idempotency keys + naive publish-after-write | Yes | No (crash window remains) | None | Low |
| Idempotency keys + transactional outbox | Yes | Yes | Relay process (lightweight) | Medium |
| Idempotency keys + CDC | Yes | Yes | CDC pipeline (Debezium/Kafka Connect) | Medium-High |
| Idempotency keys + 2PC | Yes | Yes | Distributed transaction coordinator | High, and unavailable for Stripe specifically |

---

## 9. Application to Q3

We use **deterministic idempotency keys (ours and Stripe's)** together with a **transactional outbox**. This combination closes both failure modes identified in Section 1 using only infrastructure we already operate (PostgreSQL, RabbitMQ, and a lightweight relay process), without introducing CDC tooling or a distributed transaction coordinator that the scale of this problem doesn't justify.

---

## 10. References

| Source | Author/Publisher | Link/Notes |
|--------|-------------------|------------|
| **Designing Data-Intensive Applications** | Martin Kleppmann | 2017. Covers the dual-write problem and distributed consistency broadly |
| **Transactional Outbox Pattern** | Chris Richardson, microservices.io | https://microservices.io/patterns/data/transactional-outbox.html |
| **Idempotent Requests** | Stripe API Documentation | https://stripe.com/docs/api/idempotent_requests |
| **Life Beyond Distributed Transactions: An Apostate's Opinion** | Pat Helland | https://www.ics.uci.edu/~cs223/papers/cidr07p15.pdf |
| **Debezium** | Red Hat / Debezium community | https://debezium.io/ - representative CDC tooling, evaluated and not chosen for this scope |

---

## Document Summary

Duplicate charges and lost successes are solved by different mechanisms: deterministic idempotency keys (checked locally and passed to Stripe) prevent the former; a transactional outbox, which writes the state change and the outbound event in one atomic local transaction, prevents the latter. Both were chosen over heavier alternatives (CDC, two-phase commit) because they solve the specific problem using infrastructure we already run.
