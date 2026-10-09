# Resilience Patterns Research

## Document Purpose

This document researches patterns for containing failure in a dependency we don't control - specifically Stripe, in the context of the Friday-night outage. It covers timeouts, circuit breakers, and bulkheads: what they are, where they came from, and why the combination was chosen over simpler alternatives for the Payment service.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Resilience Fundamentals](#2-resilience-fundamentals)
3. [Pattern: Timeouts](#3-pattern-timeouts)
4. [Pattern: Circuit Breaker](#4-pattern-circuit-breaker)
5. [Pattern: Bulkhead](#5-pattern-bulkhead)
6. [Technologies Evaluated](#6-technologies-evaluated)
7. [Trade-offs Analysis](#7-trade-offs-analysis)
8. [Application to Q3](#8-application-to-q3)
9. [References](#9-references)

---

## 1. The Problem We're Solving

During the Friday-night incident, Stripe doesn't go fully down - it degrades. Some requests are slow, some time out, some fail. A naive integration (call Stripe, wait, handle success/failure) has no defense against *slowness* as opposed to outright failure: every concurrent order placement ties up a thread or connection waiting on a dependency that may take ten seconds to answer, or never answer at all. This is a different, and in practice more damaging, failure mode than a clean "service unavailable" response.

---

## 2. Resilience Fundamentals

Resilience engineering for distributed dependencies generally targets three distinct risks:

1. **Latency risk** - a dependency responds, but slowly, tying up resources
2. **Availability risk** - a dependency doesn't respond at all
3. **Cascading risk** - a struggling dependency causes the caller to also struggle, which then causes the caller's callers to struggle, and so on

Michael Nygard's *Release It!* (the book most responsible for popularizing production resilience patterns in the industry) frames this as the core lesson of operating real systems: failure is not binary, and "it works" in a demo does not mean "it survives a slow, partial failure in production."

---

## 3. Pattern: Timeouts

**What it is:** A hard upper bound on how long a caller will wait for a response before giving up.

**Why it's necessary but not sufficient:** Without a timeout, a slow dependency can hold a request (and the resources behind it - threads, connections) indefinitely. But a timeout alone doesn't stop the caller from immediately trying again on the next request, repeatedly tying up resources on a dependency that's still unhealthy.

**Applied to Payment:** Every call to Stripe from the Payment service has a bounded timeout (10 seconds in our configuration - see `../Architecture/03-TechnicalSpecifications.md`). Critically, a timeout is treated as an **ambiguous** outcome, not a failure - this is the core lesson from the incident (see `02-IdempotencyAndConsistency.md` for why).

---

## 4. Pattern: Circuit Breaker

**What it is:** A circuit breaker tracks the error/timeout rate of calls to a dependency. When that rate crosses a threshold, the breaker "trips" and immediately fails new calls without attempting them at all (the `OPEN` state), giving the dependency room to recover. After a cool-down period, it allows a small number of trial requests through (`HALF-OPEN`); if those succeed, it closes again; if they fail, it stays open.

**Origin:** Martin Fowler's CircuitBreaker pattern, itself drawing on Michael Nygard's *Release It!*, which introduced the pattern to a wider software audience as a direct response to production outages caused by cascading failure.

```
CLOSED (normal) ──[error threshold crossed]──▶ OPEN (fail fast)
    ▲                                                │
    │                                      [cool-down elapses]
    │                                                ▼
    └──────[trial requests succeed]────────── HALF-OPEN (probe)
```

**Why This Matters for Payment Specifically:** Without a circuit breaker, every single order placed during the outage would independently discover that Stripe is slow, by waiting out the full timeout. With a circuit breaker open, subsequent attempts fail (or are queued as `AMBIGUOUS`/retry candidates) immediately, which keeps the Payment service itself responsive and prevents the outage from consuming all its capacity.

---

## 5. Pattern: Bulkhead

**What it is:** Named after ship design (compartments that stop one flooded section sinking the whole vessel), a bulkhead limits how much of a system's capacity a single dependency can consume - for example, a fixed-size connection pool or thread pool dedicated to Stripe calls, separate from the pool used for other work.

**Why Combined With a Circuit Breaker:** The circuit breaker stops *new* calls once Stripe is known to be unhealthy; the bulkhead limits the damage *while* that determination is being made (i.e., calls already in flight when the breaker was still closed). Used together, they address both the detection lag and the resource-starvation risk.

---

## 6. Technologies Evaluated

| Library | Ecosystem | Notes |
|---------|-----------|-------|
| **opossum** | Node.js | Lightweight, implements circuit breaker + timeout + fallback; chosen for the Payment service given the existing stack |
| **resilience4j** | Java/JVM | Comprehensive resilience toolkit (circuit breaker, bulkhead, rate limiter, retry) |
| **Polly** | .NET | Equivalent toolkit for .NET services |

All three implement the same underlying patterns; the choice is driven by runtime, not by capability differences.

---

## 7. Trade-offs Analysis

| Approach | Latency Protection | Availability Protection | Cascading Protection | Complexity |
|----------|---------------------|---------------------------|------------------------|------------|
| No protection | None | None | None | Lowest |
| Timeout only | Partial (bounds worst case) | None | None | Low |
| Timeout + retry (naive) | Partial | Partial | **Worsens it** (retries pile onto an already-struggling dependency) | Low |
| Timeout + circuit breaker | Good | Good | Good | Medium |
| Timeout + circuit breaker + bulkhead | Good | Good | Best | Medium-High |

The naive "timeout + retry" row is called out specifically because it's the most common well-intentioned mistake: retrying immediately on failure, without backoff or a circuit breaker, can turn a brief dependency slowdown into a self-inflicted denial-of-service against that same dependency - a pattern sometimes called a "retry storm."

---

## 8. Application to Q3

We chose **timeout + circuit breaker + bulkhead**, applied specifically to the Payment service's Stripe calls, and separately to the Integration Service's calls to the acquired company's platform (see `04-AntiCorruptionLayerIntegration.md`). We did not apply this pattern everywhere in the system - only at the two points where we depend on an external system we don't control. Internal calls between our own modules (Order to Menu, for instance) don't exhibit the same failure mode and don't need the same protection; adding circuit breakers there would be complexity without a corresponding problem.

---

## 9. References

| Source | Author/Publisher | Link/Notes |
|--------|-------------------|------------|
| **Release It!** (2nd Edition) | Michael T. Nygard | 2018. Origin of the circuit breaker and bulkhead patterns for production software |
| **CircuitBreaker** | Martin Fowler | https://martinfowler.com/bliki/CircuitBreaker.html |
| **opossum** | nodeshift | https://github.com/nodeshift/opossum |
| **resilience4j** | resilience4j.readme.io | https://resilience4j.readme.io/ |
| **Polly** | App-vNext | https://github.com/App-vNext/Polly |
| **The Hard Parts of Distributed Systems** | AWS Builders' Library | https://aws.amazon.com/builders-library/ - covers retries, backoff, and avoiding retry storms |

---

## Document Summary

Timeouts, circuit breakers, and bulkheads address three distinct failure risks (latency, availability, cascading) and are strongest used together. We apply this combination narrowly - to the two points in our architecture where we call a third-party system we don't control (Stripe, and the acquired company's platform) - rather than broadly across internal module calls, consistent with the principle of solving the problem we actually have.
