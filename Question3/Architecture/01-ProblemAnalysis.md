# Problem Analysis: Q2 Architecture Under Q3 Scale and Failure Conditions

## Document Purpose

This document analyzes our Q2 architecture against two pressures simultaneously: the scale Q3 introduces (50 restaurants, 3 cities, 12 developers across 2 teams) and a specific production failure scenario (a Friday-night payment provider outage) that Q2's architecture was never built to survive cleanly. The goal is to identify precisely what breaks, what merely struggles, and what still holds - before deciding what to change.

**Key Principle:** We are not redesigning from scratch. Q2 gave us Redis caching, RabbitMQ, a read replica, and feature flags. Q3 builds on that, it doesn't replace it.

---

## Table of Contents

1. [Baseline: What We Built in Q2](#1-baseline-what-we-built-in-q2)
2. [Q3 Context: What Changed](#2-q3-context-what-changed)
3. [The Incident: Problem-by-Problem Analysis](#3-the-incident-problem-by-problem-analysis)
4. [Q2 Decisions Under Q3 Scrutiny](#4-q2-decisions-under-q3-scrutiny)
5. [What Still Works](#5-what-still-works)
6. [What Breaks](#6-what-breaks)
7. [What Struggles](#7-what-struggles)
8. [Summary: Path Forward](#8-summary-path-forward)

---

## 1. Baseline: What We Built in Q2

### 1.1 Q2 Constraints (Reminder)

| Aspect | Q2 Value |
|--------|----------|
| Restaurants | 15, expecting 50 |
| Developers | 5 |
| Budget | More to invest |
| DevOps | None |
| Geography | One city |

### 1.2 Q2 Architecture Summary

**Deployment Topology:** Still a single Modular Monolith - no services extracted yet.

**New Infrastructure Added in Q2:**
- Redis cache (cache-aside, event-invalidated) for restaurant/menu browsing
- RabbitMQ for asynchronous notification processing
- PostgreSQL read replica for reporting queries
- Feature flags for deployment risk reduction
- CODEOWNERS / module ownership for the 5-developer team

**Payment in Q2:** Still an in-process module call. Order calls Payment synchronously, Payment calls Stripe synchronously. No idempotency key, no circuit breaker, no reconciliation - a direct call with simple retry-on-failure, same as Q1.

### 1.3 Q2 Design Decisions

| Decision | Rationale |
|----------|-----------|
| Keep modular monolith (no extraction yet) | 5 devs, boundaries still manageable in one deployable |
| Redis cache-aside for browsing | High read:write ratio on restaurant/menu data |
| RabbitMQ for notifications only | Notifications were the one place async genuinely helped |
| Read replica for reporting | Separates OLTP from OLAP without full CQRS |
| Payment stays in-process, synchronous | No problem had yet surfaced that justified touching it |

---

## 2. Q3 Context: What Changed

### 2.1 Q3 Growth

| Aspect | Q2 → Q3 | Growth Factor |
|--------|---------|----------------|
| Restaurants | 15 → 50 | ~3.3x |
| Geography | 1 city → 3 cities, expanding to 10 more | Major |
| Developers | 5 → 12 | 2.4x |
| Team structure | Informal ownership → 2 formal teams | Structural |
| Driver network | Larger → Much larger | Significant |
| Customer base | Larger → Much larger | Significant |

### 2.2 The Precipitating Event

Unlike Q2, which was triggered by gradually observed symptoms (slow queries, blocking notifications), Q3 is triggered by a single, sharp incident: on a Friday evening during peak volume, the payment provider (Stripe) degrades. Some requests are slow, some time out, some fail outright, and - critically - some succeed on Stripe's side without our system ever receiving the confirmation. Customers see uncertainty; some double-click "Place Order"; some restaurants get orders, some don't; some customers are charged with no visible order; support is flooded.

### 2.3 Critical Shift

**Q1:** "Does this work?"
**Q2:** "Does this scale?"
**Q3:** "Does this survive a bad night, and can a much bigger, two-team organization safely own the part that failed?"

We are no longer only asking about throughput. We're asking whether the architecture degrades gracefully under a specific, realistic external failure, and whether the team structure around that failure-prone component makes sense at 12 developers.

---

## 3. The Incident: Problem-by-Problem Analysis

### 3.1 Problem 1: A Timeout Is Not a Failure, But Q2's Code Treats It As One

**Symptom:** When a Stripe call exceeds our timeout, the Q2 code path marks the order `PAYMENT_FAILED` and tells the customer to retry - even though Stripe may have actually processed the charge.

**Why This Happens (Q2 Architecture):**
- Payment logic has no concept of an "unknown" outcome - only success or failure
- A network timeout and a genuine decline are handled identically
- Retrying after a false "failure" can produce a second, real charge

**Consequence:** This is the single root cause behind nearly every symptom described in the incident - double charges, orders restaurants never see, and customers charged with no visible order.

### 3.2 Problem 2: No Idempotency Protection Against Retries or Redelivery

**Symptom:** A customer who clicks "Place Order" twice, or a RabbitMQ message that gets redelivered after a crash, can trigger Stripe twice for the same logical payment.

**Why This Happens (Q2 Architecture):**
- Q2 never needed idempotency keys - payment was a simple, rarely-retried synchronous call
- No deterministic key ties a retried request back to the original attempt

### 3.3 Problem 3: A Crash Between "Stripe Succeeds" and "We Record It" Loses the Payment

**Symptom:** If the process crashes after Stripe confirms success but before the order is updated, the charge exists with no corresponding order progression - money taken, food never made, no visible order for the customer.

**Why This Happens (Q2 Architecture):**
- Updating our own state and notifying the rest of the system are separate, non-atomic steps
- Nothing guarantees the second step happens if the process dies between them

### 3.4 Problem 4: No Mechanism to Resolve Ambiguity After the Fact

**Symptom:** Once Stripe recovers, nothing in Q2's architecture goes back and checks "what actually happened" for the requests that timed out during the outage - those orders stay stuck or get resolved incorrectly.

**Why This Happens (Q2 Architecture):**
- There is no reconciliation process at all; the architecture assumes every payment attempt resolves immediately and correctly

### 3.5 Problem 5: Payment's Failure Mode Is Everyone's Problem

**Symptom:** Because Payment is just another module in the shared deployable, a payment-specific fix (e.g., adding a circuit breaker) requires touching and redeploying the whole monolith, and both Q3's teams have a stake in code neither fully owns.

**Why This Happens (Q2 Architecture):**
- Payment was never extracted; it has no independent failure boundary or independent release cycle
- At 5 developers this was invisible; at 12 developers across 2 teams it's a structural ownership gap

### 3.6 Problem 6: Restaurant Onboarding Is Still Manual at 50+ Restaurants

**Symptom:** Platform staff still create every restaurant account by hand - unrelated to the incident, but a growth bottleneck Q3's scale makes newly painful.

**Why This Happens:** Q1/Q2 never needed self-service onboarding; this is a feature gap inherited unchanged, not a consequence of the incident.

---

## 4. Q2 Decisions Under Q3 Scrutiny

| Q2 Decision | Still Valid at Q3? | Verdict |
|-------------|---------------------|---------|
| Modular monolith for everything | Valid for every module **except** Payment | Modify (extract Payment only) |
| Redis cache-aside for browsing | Still solves the same problem, now at larger scale | Keep |
| RabbitMQ for notifications | Still correct; becomes the backbone for Payment too | Keep, extend |
| Read replica for reporting | Still sufficient at 50 restaurants | Keep |
| Payment as synchronous in-process call | This is the decision the incident directly disproves | Replace |
| CODEOWNERS / informal module ownership | Doesn't scale cleanly to 2 formal teams around a shared failure-prone module | Modify (align ownership to new service boundary) |

---

## 5. What Still Works

- The modular monolith structure for Order, Menu, Restaurant, Delivery, Identity, Customer, and Reporting - none of these have Payment's external-failure problem
- Redis caching for restaurant/menu browsing
- RabbitMQ as infrastructure (the broker itself doesn't need to change, only how Payment uses it)
- The PostgreSQL read replica for reporting
- Feature flags for deployment risk management

## 6. What Breaks

- Treating a payment timeout as a definitive failure - this directly causes double charges and silently-lost orders
- Calling Payment in-process and synchronously from Order - this is what makes a Stripe slowdown into an Order-placement slowdown for every customer, not just the ones affected
- Having no idempotency key - this is what turns retries (customer-driven or message-redelivery-driven) into duplicate charges
- Having no durable, atomic link between "Stripe said yes" and "the rest of the system finds out" - this is what loses payments on a crash

## 7. What Struggles

- Shared ownership of the Payment module between two newly-formed teams - not a correctness bug, but a recurring source of coordination overhead
- Manual restaurant onboarding - a growing operational bottleneck, unrelated to the incident but exposed by the same scale increase

---

## 8. Summary: Path Forward

The Friday-night incident isn't really about Stripe being unreliable - any payment provider can degrade. It's about Q2's architecture having no concept of *ambiguous* outcomes, no protection against *duplicate* attempts, no *durable* guarantee that a success is recorded, and no *process* for resolving uncertainty after the fact. Q3's architecture work (detailed in `02-ArchitectureEvolution.md`) addresses exactly these four gaps by extracting Payment into its own service with idempotency, a circuit breaker, a transactional outbox, and a reconciliation worker - and uses that same extraction to resolve the team-ownership ambiguity between Q3's two teams. Everything else from Q2 stays as it is.
