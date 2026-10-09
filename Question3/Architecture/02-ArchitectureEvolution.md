# Architecture Evolution: Q2 → Q3

## Document Purpose

This document walks through each architectural change made between Q2 and Q3, in the order they were decided, with the problem each one solves and how it fits together with the others. It also covers the acquisition integration challenge, which is architecturally separate from the payment resilience work but follows the same evolutionary philosophy: extend, don't replace.

---

## Table of Contents

1. [Evolution Overview](#1-evolution-overview)
2. [Change 1: Payment Service Extraction](#2-change-1-payment-service-extraction)
3. [Change 2: Idempotency Keys](#3-change-2-idempotency-keys)
4. [Change 3: Circuit Breaker Around Stripe](#4-change-3-circuit-breaker-around-stripe)
5. [Change 4: Transactional Outbox](#5-change-4-transactional-outbox)
6. [Change 5: Reconciliation Worker](#6-change-5-reconciliation-worker)
7. [Change 6: Team Realignment](#7-change-6-team-realignment)
8. [Change 7: Integration Service for the Acquisition](#8-change-7-integration-service-for-the-acquisition)
9. [Q3 Architecture Diagram](#9-q3-architecture-diagram)
10. [What Remains Unchanged](#10-what-remains-unchanged)
11. [Migration Considerations](#11-migration-considerations)

---

## 1. Evolution Overview

Q3's changes fall into two independent efforts:

1. **Payment resilience** (Changes 1-6) - directly answers the Friday-night incident: extract Payment, make it idempotent, protect it with a circuit breaker, make its success durable, reconcile what's left ambiguous, and align team ownership to the new boundary.
2. **Acquisition integration** (Change 7) - a separate, newly-introduced requirement: absorb a smaller acquired company's systems without rewriting either platform.

These are presented together because they happened in the same growth phase, but they don't depend on each other. A reviewer evaluating only the payment incident can skip to Change 7's prerequisites; a reviewer evaluating only the acquisition can treat Changes 1-6 as "Payment is already a well-behaved standalone service" and start from there.

---

## 2. Change 1: Payment Service Extraction

**Solves:** Problem 5 from `01-ProblemAnalysis.md` (shared ownership of a module with a fundamentally different failure mode), and is the prerequisite for Changes 2-5.

**What:** Payment becomes a separately deployed service with its own PostgreSQL database. Order no longer calls Payment in-process; it publishes a `ProcessPayment` command to RabbitMQ and reacts to `PaymentSucceeded` / `PaymentFailed` / `PaymentAmbiguous` events published back.

**Why Now, and Why Only Payment:** Every other Q2 module (Order, Menu, Restaurant, Delivery, Identity, Customer, Reporting) shares the same failure characteristics as the rest of the monolith - a bug in Menu doesn't have a different blast radius than a bug in Restaurant. Payment is categorically different: it depends on a third-party network service with its own availability, and that dependency is exactly what caused the incident. Extracting it is the one extraction with a concrete, demonstrated justification.

```
Before (Q2):                              After (Q3):

Order Module                              Order Module
   │ in-process call                         │ publish ProcessPayment
   ▼                                         ▼
Payment Module ──▶ Stripe                 RabbitMQ ──▶ Payment Service ──▶ Stripe
   │                                                        │
   ▼                                                        ▼
  (same process,                                    (own process, own DB,
   same deploy)                                       own release cycle)
```

---

## 3. Change 2: Idempotency Keys

**Solves:** Problem 2 (duplicate charges from retries or message redelivery).

**What:** Every payment attempt is keyed deterministically as `order-{orderId}-payment`. This key is checked locally before calling Stripe, and passed as Stripe's own idempotency key as a second line of defense.

**Why This Order of Operations Matters:** The key has to be deterministic (not a fresh UUID per attempt), or a retry would simply generate a new key and defeat the entire protection. See `../Research/02-IdempotencyAndConsistency.md` for the options considered.

---

## 4. Change 3: Circuit Breaker Around Stripe

**Solves:** Problem 1 (a slow, failing dependency degrading the whole payment path) by failing fast once Stripe is clearly unhealthy, instead of letting every request queue up behind a slow timeout.

**What:** All calls to Stripe from the Payment service go through a circuit breaker (trip threshold on error/timeout rate, cool-down period, half-open probing). Combined with a bounded per-call timeout and a bulkhead limiting how many concurrent Stripe calls the service will attempt.

**Why It Lives Inside Payment, Not Order:** Because Payment is now a separate service, the circuit breaker is entirely invisible to Order - Order only ever sees `PaymentAmbiguous`/`PaymentFailed`/`PaymentSucceeded` events, never a raw Stripe timeout. This is a direct benefit of Change 1.

---

## 5. Change 4: Transactional Outbox

**Solves:** Problem 3 (a crash between Stripe confirming success and the rest of the system finding out).

**What:** When Stripe confirms a charge, the Payment service writes the payment's new state **and** an outbox row for the `PaymentSucceeded` event in a single local database transaction. A separate relay process publishes outbox rows to RabbitMQ and retries until each publish succeeds.

**Why Not Just Publish Directly After the DB Write:** Two separate operations (write to DB, then publish to the broker) can never be made atomic by calling them one after another - if the process dies between them, the event is lost even though the database correctly recorded success. The outbox table turns this into one atomic local transaction. See `../Research/02-IdempotencyAndConsistency.md` for the comparison against Change Data Capture as an alternative.

---

## 6. Change 5: Reconciliation Worker

**Solves:** Problem 4 (nothing resolves ambiguous payment outcomes once Stripe recovers).

**What:** A background worker polls every `payment_attempts` row left in `PENDING` or `AMBIGUOUS` state for longer than a threshold (e.g. 30 seconds), queries Stripe directly for the true outcome using the same idempotency key, and updates the local record plus publishes the resulting event. A separate daily batch job cross-checks the full `payment_attempts` table against Stripe's settlement report as a second, slower safety net.

**Why Two Layers (Real-Time + Batch):** The real-time worker resolves most ambiguity within minutes, which is what customers and support need. The daily batch catches anything the real-time path somehow missed - a belt-and-braces approach appropriate for money movement, not duplicated effort.

---

## 7. Change 6: Team Realignment

**Solves:** The organizational half of Problem 5 - 12 developers in 2 teams need ownership boundaries that match the architecture, not boundaries left over from when there were 5.

**What:**
- **Team Commerce** owns Order, Menu, Restaurant, Delivery - the core ordering-flow modules, still inside the monolith
- **Team Platform** owns Identity, Customer, Reporting, and the newly-extracted Payment service - including Payment's on-call

**Why This Split:** Payment extraction (Change 1) gives Team Platform a service they fully and exclusively own, resolving the ambiguous shared ownership that existed in Q2's CODEOWNERS approach. This is a direct, almost mechanical consequence of Change 1 - the team boundary follows the service boundary, not the other way around.

---

## 8. Change 7: Integration Service for the Acquisition

**Solves:** The separate requirement to integrate an acquired company's Customer/Restaurant/Order/Payment systems "without rewriting either platform."

**What:** A new Integration Service acts as the sole point of contact between our platform and the acquired company's systems. Our Order/Restaurant/Customer modules only ever see our own canonical domain model; the Integration Service translates to and from the acquired company's model in both directions and is the only component that calls their APIs.

```
Our Order/Restaurant/Customer modules
          │ (our canonical model only)
          ▼
  Integration Service  ◀── circuit breaker + retries, same pattern as Payment→Stripe
          │ (translates both ways)
          ▼
Acquired company's Customer/Restaurant/Order/Payment systems
```

**Why This Reuses the Same Pattern as Payment:** The acquired company's platform is, architecturally, just another external dependency we don't control - exactly like Stripe. The same containment pattern (circuit breaker, bounded retries, isolate the failure) applies directly. The translation logic (Anti-Corruption Layer) is the genuinely new piece; see `../Research/04-AntiCorruptionLayerIntegration.md` for why this approach was chosen over a shared database, a full rewrite, or point-to-point integration.

**Relationship to Changes 1-6:** None, architecturally. The Integration Service is independent of the Payment service - it was built because of a business event (the acquisition), not because of the incident. It's listed here because it's part of the same Q3 evolution phase, not because it depends on the payment work.

---

## 9. Q3 Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MODULAR MONOLITH  (Team Commerce)                        │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐                      │
│   │Restaurant│ │  Menu   │  │  Order  │  │Delivery │                      │
│   └─────────┘  └─────────┘  └─────────┘  └─────────┘                      │
└─────────────────────────────────────────────────────────────────────────────┘
     │ reads/cache     │ writes            │ publishes/consumes     │ via Integration Service
     ▼                 ▼                    ▼                        ▼
┌──────────┐    ┌──────────────┐    ┌──────────────────────┐   ┌─────────────────────┐
│  Redis   │    │ PostgreSQL   │    │ RabbitMQ (shared)     │   │ Integration Service │
│  Cache   │    │Primary+Replica│   │ - notifications queue │   │ (ACL, circuit        │
└──────────┘    └──────────────┘    │ - payments cmd/event  │   │  breaker + retries)  │
                                     └───────────┬───────────┘   └──────────┬──────────┘
                                                  │                          │
                                                  ▼                          ▼
                          ┌───────────────────────────────────┐   ┌─────────────────────┐
                          │ PAYMENT SERVICE (Team Platform)    │   │ Acquired company's  │
                          │ - Idempotency keys                 │   │ Customer/Restaurant/│
                          │ - Circuit breaker around Stripe    │   │ Order/Payment       │
                          │ - Transactional outbox              │   │ systems (untouched) │
                          │ - Reconciliation worker             │   └─────────────────────┘
                          │ - Own database                      │
                          └──────────────┬──────────────────────┘
                                         ▼
                                     Stripe

┌─────────────────────────────────────────────────────────────────────────────┐
│          MODULAR MONOLITH, continued (Team Platform)                       │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐                                   │
│   │Identity │  │Customer │  │Reporting│                                   │
│   └─────────┘  └─────────┘  └─────────┘                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 10. What Remains Unchanged

| Area | Unchanged From Q2 | Reasoning |
|------|---------------------|-----------|
| Order, Menu, Restaurant, Delivery, Identity, Customer, Reporting as monolith modules | Yes | None of them share Payment's external-failure profile |
| Redis caching | Yes | Still solves the same browsing-load problem |
| RabbitMQ as the broker | Yes (extended with new queues, not replaced) | Already proven infrastructure |
| Read replica for reporting | Yes | 50 restaurants is still within a single replica's capacity |
| Feature flags | Yes | Still the deployment-risk mechanism of choice |
| Driver model (restaurant-owned) | Yes | Business model unchanged since Q1 |

## 11. Migration Considerations

- **Payment extraction order of operations:** idempotency keys and the outbox pattern were implemented *before* the physical extraction, so the extraction itself didn't have to introduce two risky changes (new service boundary + new consistency pattern) at once.
- **Acquisition integration as a future migration seam:** because our modules never talk to the acquired company's systems directly - only through the Integration Service - a later decision to migrate acquired restaurants fully onto our platform could happen restaurant-by-restaurant behind that same interface, with no changes required elsewhere. Nothing about Q3 requires this; it's a side effect of doing the integration correctly the first time (the strangler fig pattern, discussed in `../Research/04-AntiCorruptionLayerIntegration.md`).
- **No database migration risk:** Payment's new database starts empty; historical payment records stay in the monolith's database and are read, not moved, during the transition period.
