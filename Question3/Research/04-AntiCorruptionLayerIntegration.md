# Anti-Corruption Layer and Integration Patterns Research

## Document Purpose

This document researches how to integrate an acquired company's Customer, Restaurant, Order, and Payment systems without rewriting either platform - the final-integration-challenge requirement of Q3. It covers the Anti-Corruption Layer pattern, Domain-Driven Design's bounded context concept, and the alternatives that were considered and rejected.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Integration Fundamentals: Bounded Contexts](#2-integration-fundamentals-bounded-contexts)
3. [Pattern: Anti-Corruption Layer](#3-pattern-anti-corruption-layer)
4. [Alternative Considered: Shared Database](#4-alternative-considered-shared-database)
5. [Alternative Considered: Point-to-Point Integration](#5-alternative-considered-point-to-point-integration)
6. [Alternative Considered: Full Platform Migration](#6-alternative-considered-full-platform-migration)
7. [Related Pattern: Strangler Fig](#7-related-pattern-strangler-fig)
8. [Synchronous vs Event-Carried Integration](#8-synchronous-vs-event-carried-integration)
9. [Trade-offs Analysis](#9-trade-offs-analysis)
10. [Application to Q3](#10-application-to-q3)
11. [References](#11-references)

---

## 1. The Problem We're Solving

We've acquired a smaller food-delivery company with its own Customer, Restaurant, Order, and Payment systems. The business needs these to work together with our platform - at minimum, our customers should be able to interact with acquired-company restaurants - but we've been told explicitly not to rewrite either platform. This rules out a big-bang migration and requires genuine integration between two systems that were never designed to talk to each other.

---

## 2. Integration Fundamentals: Bounded Contexts

Eric Evans' *Domain-Driven Design* introduces the concept of a **bounded context**: a boundary within which a particular domain model applies and has a consistent meaning. Two systems built independently - as our platform and the acquired company's platform were - almost certainly model the same real-world concepts (an "Order," a "Restaurant") differently: different state machines, different field names, different assumptions about what data is required when.

Integration between bounded contexts is a well-studied problem precisely because naively merging two models produces a "corrupted" result - one that inherits the inconsistencies and edge cases of both without being faithful to either.

---

## 3. Pattern: Anti-Corruption Layer

**What it is:** A dedicated translation boundary between two bounded contexts. All communication in either direction passes through this layer, which is solely responsible for converting one system's model into the other's. Neither system is modified to understand the other's concepts directly.

**Origin:** Introduced by Eric Evans in *Domain-Driven Design* (2003), and elaborated as a standalone pattern by Martin Fowler and in widespread architecture practice since. Microsoft's Azure Architecture Center also documents it as a standard integration pattern for exactly this acquisition scenario.

```
Our Order/Restaurant/Customer modules
          │ (our model only - no knowledge of the acquired platform)
          ▼
  Anti-Corruption Layer (Integration Service)
          │ (translates both directions)
          ▼
Acquired company's Customer/Restaurant/Order/Payment systems (untouched)
```

**Why It Satisfies "Don't Rewrite Either Platform":** Both systems keep running exactly as they are. The only new code is the translation layer itself - neither existing codebase is modified to accommodate the other.

---

## 4. Alternative Considered: Shared Database

**What it is:** Both platforms read and write a common set of tables directly.

**Why Rejected:** This is one of the most common ways two systems become invisibly, permanently coupled - a schema change on either side risks silently breaking the other. Gregor Hohpe and Bobby Woolf's *Enterprise Integration Patterns* discusses "Shared Database" integration at length as an approach that looks simple initially but degrades badly once the two systems evolve independently, which is exactly our situation (two platforms built by different teams, with different release cadences, now needing to coexist).

---

## 5. Alternative Considered: Point-to-Point Integration

**What it is:** Individual modules in our system call the acquired platform's APIs directly, wherever they need its data, with translation logic written inline at each call site rather than centralized.

**Why Rejected:** Without a single, dedicated translation boundary, every module that needs acquired-platform data ends up with its own ad-hoc mapping logic. This is precisely the scenario Evans describes as model "corruption" - foreign concepts leak into our domain piecemeal, with no single place to reason about, test, or update the translation when the acquired platform's API changes.

---

## 6. Alternative Considered: Full Platform Migration

**What it is:** Rewrite or migrate the acquired company's systems onto our platform immediately.

**Why Rejected:** Explicitly out of scope per the requirement. Independent of that, it's also high-risk without a demonstrated need: a large rewrite of another team's production system, under time pressure, is a well-documented way to damage both platforms' reliability at once. Sam Newman's *Monolith to Microservices* argues for incremental, strangler-style migration over big-bang rewrites for exactly this class of risk.

---

## 7. Related Pattern: Strangler Fig

**What it is:** Named by Martin Fowler after the strangler fig vine, which grows around a host tree and gradually replaces it - a migration pattern where new functionality is built alongside an old system and traffic is incrementally redirected, rather than attempting a single cutover.

**Relationship to the Anti-Corruption Layer:** These are complementary, not competing, patterns. An ACL built for integration (our actual requirement) happens to also create the exact seam a future strangler fig migration would need: because our modules only ever talk to our canonical model through the Integration Service, a later decision to migrate an acquired restaurant fully onto our platform could happen behind that same interface, one restaurant at a time, with zero changes to the rest of the system. We are not building a strangler fig migration in Q3 - nothing in the brief asks for one - but the ACL we are building doesn't foreclose it either.

---

## 8. Synchronous vs Event-Carried Integration

Hohpe and Woolf's *Enterprise Integration Patterns* distinguishes request-response integration from event-carried state transfer (replicating relevant state via events ahead of time, so reads never block on a live call to the other system).

| Use Case | Pattern Chosen | Why |
|----------|------------------|-----|
| Browsing acquired-company restaurants/menus | Synchronous read-through, cached | Read-heavy, latency-sensitive, tolerates a short cache TTL (same cache-aside approach as Q2's own restaurant browsing) |
| Order status updates flowing back from the acquired platform | Event-carried (their webhooks translated into our event schema) | State changes after the fact; no need for a live round trip |

We deliberately mix the two rather than applying either purely everywhere - full event-carried state transfer for restaurant/menu data would mean maintaining a complete replicated copy of their catalog with its own freshness and consistency concerns, more than the problem currently justifies.

---

## 9. Trade-offs Analysis

| Option | Satisfies "No Rewrite" | Data Ownership Clarity | Coupling | Future Migration Path |
|--------|---------------------------|----------------------------|----------|---------------------------|
| Shared database | Yes (no rewrite) but couples schemas | Poor - unclear/contested ownership | High, invisible | Poor - schemas are entangled |
| Point-to-point, no ACL | Yes | Poor - mapping logic scattered | Medium-high | Poor - no single seam |
| Anti-Corruption Layer (chosen) | Yes | Clear - each platform owns its own data | Low - only through the ACL | Good - ACL doubles as a strangler fig seam |
| Full migration | No - explicitly violates the requirement | N/A | N/A | N/A |

---

## 10. Application to Q3

We build a single Integration Service as the Anti-Corruption Layer between our platform and the acquired company's. It is the only component that calls the acquired platform's APIs, and the only place translation logic lives. It uses the same circuit-breaker-and-retry containment pattern researched in `01-ResiliencePatterns.md`, since the acquired platform is, architecturally, just another external dependency we don't control. Data ownership stays clean - each platform owns its own data, and the Integration Service holds nothing beyond a small ID-mapping table and short-lived caches.

---

## 11. References

| Source | Author/Publisher | Link/Notes |
|--------|-------------------|------------|
| **Domain-Driven Design** | Eric Evans | 2003. Origin of bounded contexts and the Anti-Corruption Layer |
| **Enterprise Integration Patterns** | Gregor Hohpe, Bobby Woolf | 2003. Shared Database anti-pattern, event-carried state transfer |
| **Anti-Corruption Layer** | Martin Fowler (bliki) | https://martinfowler.com/bliki/AntiCorruptionLayer.html |
| **Anti-corruption Layer pattern** | Microsoft Azure Architecture Center | https://learn.microsoft.com/en-us/azure/architecture/patterns/anti-corruption-layer |
| **StranglerFigApplication** | Martin Fowler | https://martinfowler.com/bliki/StranglerFigApplication.html |
| **Strangler Fig pattern** | Microsoft Azure Architecture Center | https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig |
| **Monolith to Microservices** | Sam Newman | 2019. Incremental migration over rewrites |
| **Implementing Domain-Driven Design** | Vaughn Vernon | 2013. Practical application of bounded contexts and ACL |

---

## Document Summary

An Anti-Corruption Layer - implemented as a single Integration Service that owns all translation between our domain model and the acquired company's - satisfies the "don't rewrite either platform" requirement while keeping data ownership clean and coupling low. It was chosen over a shared database (which couples schemas invisibly), point-to-point integration (which scatters translation logic), and full migration (explicitly out of scope), and it happens to also create the seam a future strangler-fig migration would need, without that being a goal of this phase.
