# Reconciliation Strategies Research

## Document Purpose

This document researches how to resolve payment outcomes that were genuinely ambiguous at the time (the `AMBIGUOUS` state introduced in Q3), once the payment provider recovers. It covers webhook-driven, polling-driven, and batch reconciliation, and explains why the architecture uses more than one of them together.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Reconciliation Fundamentals](#2-reconciliation-fundamentals)
3. [Approach: Webhooks](#3-approach-webhooks)
4. [Approach: Active Polling](#4-approach-active-polling)
5. [Approach: Batch Settlement Reconciliation](#5-approach-batch-settlement-reconciliation)
6. [Trade-offs Analysis](#6-trade-offs-analysis)
7. [Application to Q3](#7-application-to-q3)
8. [References](#8-references)

---

## 1. The Problem We're Solving

A circuit breaker and idempotency keys prevent us from making the incident *worse* (no duplicate charges, no requests hammering a dead dependency). Neither of them, on its own, resolves the orders that were left in an ambiguous state *during* the outage. Once Stripe recovers, something has to go back and determine, for each `AMBIGUOUS` payment attempt, what actually happened - and update the order accordingly.

---

## 2. Reconciliation Fundamentals

Reconciliation, in a payments context, means comparing two independent records of truth - "what we think happened" and "what the payment provider's system of record says happened" - and resolving any differences. This is standard practice in financial systems generally (bank reconciliation, ledger reconciliation) long before it became a software architecture concern; the same discipline applies to any system that moves money through a third party.

---

## 3. Approach: Webhooks

**What it is:** Stripe calls our system back (an HTTP POST to a URL we register) whenever a payment's status changes, including asynchronously after the fact.

**Why It's Necessary But Not Sufficient Alone:** Webhooks are the lowest-latency way to learn about a status change, and we do consume them. But webhook delivery can itself fail, be delayed, or be missed - particularly during the exact kind of provider-side degradation described in the incident, when the provider's own outbound webhook delivery may also be affected. Stripe's own documentation advises against treating webhooks as the sole source of truth for exactly this reason, recommending they be used as a trigger to check state rather than as guaranteed delivery.

---

## 4. Approach: Active Polling

**What it is:** Our own reconciliation worker actively queries Stripe for the status of any payment attempt left `PENDING` or `AMBIGUOUS` past a threshold, rather than waiting passively for a webhook.

**Why This Is the Primary Mechanism:** Because we control the polling schedule and can guarantee it runs regardless of whether a webhook was delivered, this closes the gap webhooks leave open. It resolves most ambiguity within a short, bounded window (our implementation checks every 15 seconds for attempts older than 30 seconds - see `../Architecture/03-TechnicalSpecifications.md`).

---

## 5. Approach: Batch Settlement Reconciliation

**What it is:** A slower, independent process (run daily) that compares our complete `payment_attempts` table against Stripe's own settlement report for the same period - a full, authoritative accounting of every transaction Stripe processed.

**Why a Second, Independent Layer:** The real-time polling worker and the batch job have different failure assumptions. The polling worker could itself have a bug, be down during the exact window it was needed, or query the wrong criteria. The batch job is a structurally independent check - it doesn't rely on the real-time path having worked correctly, catching anything that path might have missed. This "defense in depth" approach is standard practice in systems that move money, where the cost of a silent discrepancy (unnoticed lost revenue, or an uncharged order silently fulfilled) is high enough to justify redundant verification.

---

## 6. Trade-offs Analysis

| Approach | Latency to Resolve | Resilience to Delivery Failure | Resilience to Logic Bugs | Operational Cost |
|----------|----------------------|-----------------------------------|------------------------------|---------------------|
| Webhooks only | Low (when delivered) | Low - single point of failure | Low | Low |
| Active polling only | Low-medium (bounded by poll interval) | High - doesn't depend on provider delivering anything | Medium - same code path every time | Low-medium |
| Batch reconciliation only | High (up to 24h) | High | High - independent implementation | Low |
| Webhooks + active polling + batch (chosen) | Low, with independent backstops | High | High | Medium |

---

## 7. Application to Q3

We use all three together, each covering a gap the others leave open: webhooks for the fastest-possible notification when they do arrive, active polling as the primary, reliable mechanism that doesn't depend on provider-side delivery, and daily batch reconciliation as a structurally independent final check. This layered approach is proportionate to the fact that the records being reconciled represent real money, which justifies redundancy that would be excessive for most other kinds of data.

---

## 8. References

| Source | Author/Publisher | Link/Notes |
|--------|-------------------|------------|
| **Reconciliation with Stripe / Connect account balances** | Stripe Documentation | https://stripe.com/docs/connect/account-balances#reconciliation |
| **Stripe Webhooks - best practices** | Stripe Documentation | https://stripe.com/docs/webhooks - recommends against treating webhooks as sole source of truth |
| **Designing Data-Intensive Applications** | Martin Kleppmann | 2017. General treatment of reconciling divergent system state |

---

## Document Summary

No single reconciliation mechanism is sufficient on its own: webhooks can fail to deliver, active polling depends on correct implementation, and batch reconciliation alone is too slow to resolve customer-facing ambiguity quickly. Layering all three - fast webhook consumption, a reliable active-polling worker as the primary mechanism, and an independent daily batch check - gives both low latency and high confidence, which is the appropriate standard for reconciling financial transactions.
