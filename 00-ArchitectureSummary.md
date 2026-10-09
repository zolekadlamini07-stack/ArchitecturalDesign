> **Superseded:** the final, reworked answers (modular monolith + vertical slices) are in [`FinalAnswers/`](FinalAnswers/README.md). This file is kept as history. See `FinalAnswers/CHANGES-AND-REASONS.md` for what changed and why.

# Food Delivery Platform - Architecture Summary

## Question 1: Design the Architecture

This document provides an overview of the architecture design for the food delivery platform, with links to detailed documentation.

---

## Project Context

| Aspect | Value |
|--------|-------|
| **Restaurants** | 1 (initially) |
| **Drivers** | Small number (employed by restaurant) |
| **Customers** | Small initial base |
| **Development Team** | 3 developers |
| **Budget** | Very limited |
| **DevOps** | None |
| **Goal** | Prove the business model first |

---

## Architecture at a Glance

### Deployment Model

**Modular Monolith** - Single deployable application with clear internal module boundaries and a shared database.

### Internal Code Structure

**Pragmatic Layered/Clean Architecture** with **Domain-Driven Design** modeling principles.

### Components

| # | Component | Responsibility | Data Owned |
|---|-----------|----------------|------------|
| 1 | Identity/Auth | Authentication for all actors | Users, credentials, sessions |
| 2 | Customer | Customer profiles | Profiles, addresses |
| 3 | Restaurant | Restaurant management | Profiles, settings |
| 4 | Menu | Menu management | Items, prices, availability |
| 5 | Order | Order lifecycle | Orders, items, state history |
| 6 | Payment | Payment processing | Payment records |
| 7 | Delivery | Driver and delivery management | Drivers, shifts, deliveries |
| 8 | Reporting | Business metrics | None (reads from others) |

### Communication Pattern

- Direct in-process calls between modules
- Shared database with module-owned tables
- Interfaces for external dependencies (payment provider)

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Deployment topology | Modular Monolith | 3 devs, limited budget, no DevOps |
| Payment timing | After restaurant accepts | Avoids refund complexity |
| Driver model | Owned by restaurants | Matches business model, simpler system |
| Internal structure | Pragmatic Layered + DDD | Testable, evolvable, team-appropriate |
| External dependencies | Wrapped in interfaces | Easy to swap, easy to test |

---

## Known Risks

| Risk | Mitigation |
|------|------------|
| Module coupling over time | Enforce table ownership, use interfaces, code review |
| Payment provider dependency | Wrapped in module, simple failure handling |
| Single database performance | Acceptable for MVP, add indexing, monitor |

---

## Deliberate Omissions

| What We're NOT Doing | Why |
|---------------------|-----|
| Real-time GPS tracking | Complexity not justified for MVP |
| Customer ratings/reviews | 1 restaurant doesn't need differentiation |

---

## Documentation Index

### Document 1: User Flow Diagrams
**File:** `01-UserFlowDiagrams.md`

**Contains:**
- Customer journey (register → browse → order → track → history)
- Restaurant journey (onboard → setup → daily operations → reporting)
- Driver journey (onboard → shift → deliveries → history)
- Business/Platform journey
- Actor relationships diagram

**Use this when:** You need to understand what each user type does in the system.

---

### Document 2: System Flowchart
**File:** `02-SystemFlowchart.md`

**Contains:**
- Complete order flow with all three actors (Customer, Restaurant, Driver)
- Decision points (accept/reject, payment success/fail)
- Order state machine diagram
- Notification flow
- Queue management (FIFO for incoming/preparation)
- Delivery state flow
- Payment flow detail
- Order total calculation formula

**Use this when:** You need to understand the complete lifecycle of an order, including all decision points and state transitions.

---

### Document 3: Technical Documentation
**File:** `03-TechnicalDocumentation.md`

**Contains:**
- System overview and constraints
- All 8 components with detailed responsibilities
- Data models (12 entities with all fields)
- Entity Relationship Diagram (ERD)
- Relationship summary table
- Component communication patterns
- External systems integration (payment provider)
- Authentication flow with user type routing
- Business requirements fulfillment mapping
- 3 architectural decisions (with rationale)
- 3 risks (with mitigation)
- 2 deliberate omissions (with reasoning)

**Use this when:** You need detailed technical specifications, data models, or architectural decision records.

---

### Document 4: Internal Architecture Comparison
**File:** `04-InternalArchitectureComparison.md`

**Contains:**
- Detailed comparison of 4 patterns:
  - Simple & Evolving
  - Layered/Clean Architecture
  - Vertical Slice Architecture
  - Hexagonal/Ports and Adapters
- Code examples for "Place Order" in each pattern
- Folder structures for each pattern
- Ceremony level comparison
- Pros and cons tables
- Scalability analysis for Question 2 and 3 scenarios
- Comparison matrices (files, coupling, testability, speed)
- **Recommended approach: Pragmatic Layered/Clean**
- Complete justification for the recommendation
- Defense arguments against each alternative
- Domain-Driven Design as modeling approach (integrated with Clean Architecture)
- DDD concepts applied to our system (entities, value objects, aggregates, etc.)
- References and further reading

**Use this when:** You need to understand why we chose Pragmatic Layered/Clean, or when onboarding new team members on the architecture philosophy.

---

### Document 5: Deployment Topology Research
**File:** `05-DeploymentTopologyResearch.md`

**Contains:**
- What is Deployment Topology?
- Traditional Monolith (definition, characteristics, advantages, disadvantages, Big Ball of Mud problem)
- Microservices Architecture (characteristics, principles, infrastructure requirements, 8 Fallacies of Distributed Computing)
- Modular Monolith (definition, enforcing boundaries, when to use)
- Comparison matrix (feature comparison, infrastructure requirements, team size guidance)
- Decision framework with flowchart
- Common misconceptions debunked
- Industry perspectives (ThoughtWorks, Amazon, Netflix, Shopify, Basecamp)
- Application to Question 1 with justification
- References (books, articles, videos, tools)

**Use this when:** You need to understand what monoliths, microservices, and modular monoliths actually ARE, independent of our specific application. Good for learning or explaining architectural choices to stakeholders.

---

### Document 6: Internal Architecture Research
**File:** `06-InternalArchitectureResearch.md`

**Contains:**
- What is Internal Architecture?
- Traditional Layered Architecture (origins, structure, advantages, disadvantages, dependency problem)
- Clean Architecture (Uncle Bob's circles, dependency rule, folder structure)
- Hexagonal Architecture / Ports and Adapters (driving vs driven, ports vs adapters)
- Vertical Slice Architecture (feature-first organization)
- Comparison of all patterns
- The Pragmatic Layered Approach (what it is, when to use interfaces)
- Domain-Driven Design integration (tactical patterns, entities vs anemic model, aggregates, value objects)
- Why Pragmatic Layered pairs well with Modular Monolith
- Application to Question 1
- References (books, articles, videos, example repositories)

**Use this when:** You need to understand what Clean Architecture, Hexagonal Architecture, or Vertical Slices actually ARE. Good for learning, comparing patterns, or justifying internal code organization choices.

---

### Visual Diagrams (draw.io)

| File | Description |
|------|-------------|
| `diagrams/01-CustomerFlow.drawio` | Customer journey flowchart |
| `diagrams/02-RestaurantFlow.drawio` | Restaurant onboarding and operations |
| `diagrams/03-DriverFlow.drawio` | Driver shift and delivery flow |
| `diagrams/04-SystemDiagram.drawio` | Complete system flow with 4 swimlanes |
| `diagrams/05-ERD.drawio` | Entity Relationship Diagram (12 entities) |
| `diagrams/06-ArchitectureDiagram.drawio` | Full system architecture |

**Use these when:** You need visual representations for presentations, documentation, or understanding the system at a glance. Open with diagrams.net (draw.io).

---

## Quick Reference

### Order States

```
PENDING_ACCEPTANCE → ACCEPTED → PREPARING → READY → OUT_FOR_DELIVERY → DELIVERED → COMPLETED
                  ↘ REJECTED
                              ↘ PAYMENT_FAILED
```

### Delivery States

```
ASSIGNED → ACKNOWLEDGED → PICKED_UP → IN_ROUTE → DELIVERED
```

### User Types

| Type ID | Actor | Registration |
|---------|-------|--------------|
| 1 | Restaurant | Platform onboards |
| 2 | Customer | Self-registration |
| 3 | Driver | Restaurant onboards |

### Order Total Formula

```
items_total   = SUM(item_price × quantity)
delivery_fee  = restaurant.delivery_fee
platform_fee  = (items_total + delivery_fee) × 10%
total_amount  = items_total + delivery_fee + platform_fee
```

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           MODULAR MONOLITH                                      │
│                                                                                 │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐             │
│   │Identity │  │Customer │  │Restaurant│ │  Menu   │  │  Order  │             │
│   │  Auth   │  │         │  │         │  │         │  │         │             │
│   └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘             │
│        │            │            │            │            │                   │
│   ┌────┴────┐  ┌────┴────┐  ┌────┴────┐                                       │
│   │ Payment │  │Delivery │  │Reporting│                                       │
│   │         │  │         │  │         │                                       │
│   └────┬────┘  └────┬────┘  └────┬────┘                                       │
│        │            │            │                                             │
│        │            │            │                                             │
│   ─────┴────────────┴────────────┴─────────────────────────────────────────   │
│                            SHARED DATABASE                                     │
│   ─────────────────────────────────────────────────────────────────────────   │
│                                                                                 │
└─────────────────────────────────────────────────────────────────────────────────┘
                │
                │ (Interface)
                ▼
        ┌───────────────┐
        │   Payment     │
        │   Provider    │
        │   (Stripe)    │
        └───────────────┘
```

---

## Internal Module Structure (Pragmatic Layered + DDD)

```
src/modules/order/
├── presentation/           # Controllers, API endpoints
│   └── OrderController.ts
│
├── application/            # Use cases, orchestration
│   ├── PlaceOrderUseCase.ts
│   └── AcceptOrderUseCase.ts
│
├── domain/                 # Business logic (DDD)
│   ├── entities/
│   │   └── Order.ts
│   ├── value-objects/
│   │   ├── OrderItem.ts
│   │   └── Money.ts
│   ├── services/
│   │   └── OrderPricingService.ts
│   └── repositories/
│       └── IOrderRepository.ts
│
└── infrastructure/         # External, database
    └── PostgresOrderRepository.ts
```

---

## Next Steps

1. **Review** this architecture with the team
2. **Implement** the MVP following the documented patterns
3. **Revisit** in Question 2 when growth exposes real problems
4. **Evolve** the architecture based on actual pain points, not speculation

---

## Questions 2 and 3 Preview

This architecture is designed to evolve:

| Growth Phase | Expected Changes |
|--------------|------------------|
| Question 2 (15 restaurants) | Add caching, async processing, stronger module boundaries |
| Question 3 (50 restaurants, 12 devs) | Extract services if needed, handle payment provider failures gracefully |

The modular structure and clean boundaries make these evolutions possible without rewriting.
