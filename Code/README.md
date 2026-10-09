# The Architecture, Built - C# / ASP.NET Core (.NET 10)

This folder turns the answers in `FinalAnswers/` into working code, so that when the conversation gets technical you can **open a file and see the idea**. Every file is heavily commented, and the comments use the same words and decision numbers (D1-D29, C1-C14) as the documents and slides.

```
Code/
  Phase1-Q1/   Question 1  - modular monolith + vertical slices (1 process, 1 database)
  Phase2-Q2/   Question 2  - same monolith + Redis cache, outbox + PostgreSQL job queue, Worker process, read replica, feature flags
  Phase3-Q3/   Question 3  - same topology + payment ledger, idempotency keys, circuit breaker, reconciliation
                 + Challenge - PartnerIntegration module (anti-corruption layer)
```

Each phase is a **complete, separate solution**. Phase 2 started as a copy of Phase 1 and Phase 3 as a copy of Phase 2, so **comparing the same file across phases shows exactly what changed and why**. For example, open `PlaceOrder.cs` in all three.

All three phases were built, unit-tested and **run end to end against real PostgreSQL and Redis**: the full order flow, the Q2 background processing, the Friday-night failure scenarios and the acquisition integration.

---

## Technologies used, and why (each one is in the DECISION-LOG)

| Technology | Used for | Phase |
|-----------|----------|-------|
| **C# / ASP.NET Core minimal APIs** | HTTP endpoints, one per vertical slice (no MVC views, as requested) | 1-3 |
| **PostgreSQL** (+ Npgsql) | One database, one schema per module (D4) | 1-3 |
| **Entity Framework Core** (+ snake_case naming) | Data access for slices with business rules. Simple reads and reports use plain SQL | 1-3 |
| **JWT bearer tokens** | Login. The token carries role + restaurant id | 1-3 |
| **Redis** (`IDistributedCache`) | Cache-aside for browse and menu (D11) | 2-3 |
| **PostgreSQL job queue** (hand-written, `FOR UPDATE SKIP LOCKED`) + **transactional outbox** | Durable events and background jobs, with no message broker (D12) | 2-3 |
| **.NET Worker Service** | The second process (D13) | 2-3 |
| **Polly** | Timeout + circuit breaker around the payment provider and the partner API (D21) | 3 |
| **xUnit v3** | Architecture tests (D3) + business-rule tests | 1-3 |
| **Docker Compose** | Runs PostgreSQL (+ Redis) locally | 1-3 |

---

## Where to SEE each concept in code

| Concept (from the slides/docs) | Open this file |
|--------------------------------|----------------|
| **Modular monolith**: one deployable, modules plugged in | `Phase1-Q1/src/Host/FoodDelivery.Api/Program.cs` |
| **A module's contract** with the host | `Phase1-Q1/src/BuildingBlocks/IModule.cs` |
| **Public API vs internals** (the compiler enforces the walls with `internal`) | any `PublicApi/` folder, e.g. `Phase1-Q1/src/Modules/Restaurants/PublicApi/IRestaurantsApi.cs` |
| **Vertical slice**: endpoint + request + handler in one place | `Phase1-Q1/src/Modules/Ordering/Features/PlaceOrder/PlaceOrder.cs` |
| **Simple slices stay simple** (no layers for a plain read) | `Phase1-Q1/src/Modules/Ordering/Features/Queries/OrderQueries.cs` |
| **Aggregate + state machine** (the "pragmatic" shared domain) | `Phase1-Q1/src/Modules/Ordering/Domain/Order.cs` |
| **Value object** (`Money`) | `Phase1-Q1/src/BuildingBlocks/Money.cs` |
| **Schema per module** | any `Data/schema.sql` |
| **Ports & adapters** (payment provider) | `Phase1-Q1/src/Modules/Payments/Provider/` |
| **Authorise → capture → void** | `Phase1-Q1/src/Modules/Payments/Features/` |
| **In-process events** (and their weakness) | `Phase1-Q1/src/BuildingBlocks/Events.cs` |
| **Breaking a circular dependency with events** | `Phase1-Q1/src/Modules/Delivery/PublicApi/DeliveryPublicApi.cs` |
| **Drivers belong to restaurants; first to accept wins** | `Phase1-Q1/src/Modules/Delivery/Features/AcceptDelivery/AcceptDelivery.cs` |
| **Architecture tests** | `Phase1-Q1/tests/FoodDelivery.Tests/ArchitectureTests.cs` |
| **Q1 Risk R2** (timeout treated as failure) | `Phase1-Q1/src/Modules/Payments/Features/Authorise/Authorise.cs` |
| **Transactional outbox + dual-write problem** | `Phase2-Q2/src/BuildingBlocks/Outbox.cs` |
| **PostgreSQL job queue, retries, backoff, jitter, dead letter** | `Phase2-Q2/src/BuildingBlocks/Jobs.cs` |
| **Worker process** (same code, second start command) | `Phase2-Q2/src/Host/FoodDelivery.Worker/Program.cs` |
| **Notifications leave the request** (the dependency arrow flips) | `Phase2-Q2/src/Modules/Notifications/NotificationsModule.cs` |
| **Cache-aside + invalidation** | `Phase2-Q2/src/Modules/Restaurants/Caching/RestaurantCache.cs` |
| **Read replica + daily summary** | `Phase2-Q2/src/Modules/Reporting/ReportingModule.cs` |
| **Feature flags** | `Phase2-Q2/src/BuildingBlocks/FeatureFlags.cs` + `Restaurants/Features/Onboarding/` |
| **Eventual consistency** handled in a slice | `Phase2-Q2/src/Modules/Customers/Features/Addresses/AddressSlices.cs` |
| **DB role per module, CODEOWNERS, CI** | `Phase2-Q2/db-roles.sql`, `Phase2-Q2/.github/` |
| **Order first, 202 Accepted, client idempotency key, one pending order** | `Phase3-Q3/src/Modules/Ordering/Features/PlaceOrder/PlaceOrder.cs` |
| **UNKNOWN state, payment ledger, append-only log** | `Phase3-Q3/src/Modules/Payments/Data/` |
| **The Friday fix line by line** | `Phase3-Q3/src/Modules/Payments/Features/Authorise/AuthoriseOnOrderPlaced.cs` |
| **Circuit breaker + timeout** | `Phase3-Q3/src/Modules/Payments/Provider/ResilientPaymentProvider.cs` |
| **Idempotency keys sent to Stripe** | `Phase3-Q3/src/Modules/Payments/Provider/StripePaymentProvider.cs` |
| **Reconciliation: webhook / sweeper / settlement** | `Phase3-Q3/src/Modules/Payments/Features/Reconciliation/` |
| **Compensation (void a late hold)** | `Phase3-Q3/src/Modules/Payments/Features/CaptureAndVoid/CaptureAndVoid.cs` + `Ordering/Features/PaymentOutcome/` |
| **Bulkhead** (queues with their own concurrency) | `Phase3-Q3/src/BuildingBlocks/Jobs.cs` (`JobQueues`, `RunLaneAsync`) |
| **Optimistic concurrency** (no stale overwrite of the truth) | `Phase3-Q3/src/Modules/Payments/Data/PaymentsDbContext.cs` |
| **Contracts project** (two modules listening to each other without a cycle) | `Phase3-Q3/src/Modules/Payments.Contracts/` |
| **Support timeline** ("what really happened?") | `Phase3-Q3/src/Modules/Ordering/Features/GetOrderTimeline/GetOrderTimeline.cs` |
| **City as data** | `Phase3-Q3/src/Modules/Restaurants/Data/schema.sql` |
| **Two teams** | `Phase3-Q3/.github/CODEOWNERS` |
| **Anti-corruption layer** | `Phase3-Q3/src/Modules/PartnerIntegration/` (start with the `.csproj` comment) |
| **Their model vs ours / model translation** | `PartnerIntegration/Adapter/PartnerPlatform.cs` + `Translation/PartnerTranslator.cs` |
| **ID mapping** | `PartnerIntegration/Data/IntegrationStore.cs` |

---

## How to run a phase

Prerequisites: **.NET 10 SDK** and **Docker Desktop**.

```bash
cd Code/Phase1-Q1                     # or Phase2-Q2 / Phase3-Q3
docker compose up -d                  # PostgreSQL (and Redis from Phase 2)
dotnet test                           # architecture + business-rule tests
dotnet run --project src/Host/FoodDelivery.Api
# Phase 2 and 3 only - in a SECOND terminal:
dotnet run --project src/Host/FoodDelivery.Worker
```
Then open that phase's `requests.http` (VS Code "REST Client" extension, Rider or Visual Studio) and run the requests top to bottom. The API's OpenAPI description is at `/openapi/v1.json`.

| Phase | API | PostgreSQL | Redis |
|-------|-----|-----------|-------|
| 1 | http://localhost:5080 | 5433 | - |
| 2 | http://localhost:5081 | 5434 | 6380 |
| 3 | http://localhost:5082 | 5435 | 6381 |

Different ports mean all three can run side by side. Seeded admin login: `admin@fooddelivery.local` / `Admin123!` (development only). The fake payment provider declines payment token `tok_decline`.

**Simulating Friday night (Phase 3):** switch the fake provider's behaviour while everything runs:
`PUT /admin/feature-flags/fake-provider-timeout-but-succeeds?enabled=true` (or `fake-provider-down`, `fake-provider-slow`). Then place an order and watch the Worker console.

---

## Deliberate simplifications (so the architecture stays the focus)

- **No migrations framework**: each module's `schema.sql` runs at start-up with `IF NOT EXISTS`. In production you would use migrations with expand/contract steps (D15).
- **Fake adapters** for the payment provider and the acquired company, so everything runs offline. A real **Stripe adapter** is included for comparison but isn't exercised.
- **Locally the "read replica" is the same database**, and the notifier writes to the log instead of a phone.
- **Not built in code:** combined partner reporting (Challenge section 4.3) and the Q2 "measure with pg_stat_statements" step (that's an operational practice, but the indexes it would lead to are in the schemas).
