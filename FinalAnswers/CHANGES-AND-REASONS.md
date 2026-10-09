# Changes and Reasons - what was reworked from the earlier material, and why

> The earlier work (root `*.md` files, `Question2/`, `Question3/`, `diagrams/`) is **kept unchanged** as history and deep research.
> `FinalAnswers/` is the reworked, final version. This file lists **every decision that changed**, what it was, what it is now, and the technical reason, so you can explain the thinking, not just the result.

## Summary table

| # | Area | Before (earlier material) | Now (FinalAnswers) | Stage |
|---|------|---------------------------|--------------------|-------|
| C1 | Internal code structure | Pragmatic **Layered** + DDD inside each module | Pragmatic **Vertical Slices** inside each module (+ small shared domain per module) | Q1 |
| C2 | Payment timing | Charge **after** restaurant accepts | **Authorise** at checkout → **capture** on accept → **void** on reject | Q1 |
| C3 | Module list | Separate **Menu** module (8 modules + Notification "utility") | Menu merged into **Restaurants**. Notifications is a proper module (still 8) | Q1 |
| C4 | Driver flow | Restaurant **assigns** a driver, driver **acknowledges** | Delivery **request** to the restaurant's on-shift drivers, driver **accepts** (drivers still belong to restaurants) | Q1 |
| C5 | Module communication | Direct calls only | Direct calls for commands **+ in-process events** for facts | Q1 |
| C6 | Deliberate omissions | GPS tracking, ratings/reviews | **Microservices/broker**, GPS tracking (ratings moved to "also not done") | Q1 |
| C7 | Background processing | **RabbitMQ** | **Transactional outbox + PostgreSQL-backed job queue** + worker process | Q2 |
| C8 | Deployment disruption | Feature flags (chosen *instead of* blue-green) | **Rolling deploys on 2+ instances + expand/contract migrations + CI**, with feature flags as a supplement | Q2 |
| C9 | Payments topology | Payments **extracted** into a separate service | Payments **stays a module**. Resilience is built inside it | Q3 |
| C10 | Order/payment states | `PAYMENT_AMBIGUOUS`, `REFUND_REQUIRED` as **order** states | Ambiguity lives in **Payments** (`UNKNOWN` attempt). The order just stays `PAYMENT_PENDING`. Refunds/voids are payment states | Q3 |
| C11 | Order vs payment ordering | Order created before payment, but payment called synchronously | **Order first**, payment **asynchronous via outbox → worker** | Q3 |
| C12 | Acquisition integration | Separate **Integration Service** | **PartnerIntegration module** (ACL) inside the monolith, with an explicit scope and data-ownership split | Challenge |
| C13 | Diagrams | Coloured diagrams, layered labels, one per topic | **New plain black-and-white set**, same layout across Q1/Q2/Q3 so the evolution is visible, with `[NEW]`/`[CHANGED]` tags | All |
| C14 | Old files | - | Short banner added at the top of the old answer files pointing here. Content otherwise untouched | All |

---

## Details

### C1 - Layered → Vertical Slices (the big one)
- **Before:** each module had `presentation/ application/ domain/ infrastructure/` folders.
- **Now:** each module has `features/<use-case>/` folders (endpoint + request + handler + data access + test), one small shared `domain/` for real rules (e.g. the `Order` state machine), and one public `XxxApi.ts` + `events.ts`.
- **Technical reason:**
  1. **Direction of cut.** A modular monolith already cuts the system *vertically* by business capability. Layers then cut each module *horizontally*. Slices keep the same vertical direction at both levels, so a feature lives in one place.
  2. **Change locality.** With layers, adding "accept order" edits 4 folders and the same shared `OrderService`. With slices it's a new folder. That directly reduces Q2's "developers increasingly working in the same areas" problem.
  3. **Right-sized complexity.** Layers force every request through every layer (even a trivial read). Slices let `ListMyOrders` be one query and `PlaceOrder` be rich.
- **What stayed (the pragmatic part):** shared domain model per module, ports & adapters at external systems, DDD bounded contexts as modules.
- **Where:** Q1 sections 2 and 6, DECISION-LOG D2, diagram `Q1-Architecture.drawio` page 2 (before/after side by side).

### C2 - Payment after accept → authorise / capture / void
- **Before:** the customer places the order without paying. When the restaurant accepts, we charge.
- **Problem with it:** the restaurant may start cooking before we know the card works, and if the charge fails the customer may have left the app. The brief's Q3 scenario ("customers click Place Order multiple times, unsure if their payment went through") also assumes payment happens **at Place Order**.
- **Now:** hold the money at checkout, capture on accept, void on reject. No refunds for rejections, the card is verified before the restaurant is bothered, and it matches the Q3 scenario.
- **Where:** Q1 Decision 3, DECISION-LOG D6.
- **Knock-on:** the old user-flow documents (`01-UserFlowDiagrams.md`, `02-SystemFlowchart.md`, `diagrams/01-CustomerFlow.drawio`, `diagrams/04-SystemDiagram.drawio`) still show "payment after accept". Everything else in those flows is still valid. Only the payment step moved earlier.

### C3 - Menu merged into Restaurants; Notifications is a module
- **Reason (Menu):** same owner (the restaurant), same reasons to change, and the same read path ("show me this restaurant and its menu"). Two modules created a cross-module call on the busiest read in the system (which Q2 then had to cache) for no benefit. A boundary should separate things that change for *different* reasons.
- **Reason (Notifications):** it owns data (`notification_log`), has its own external system and becomes a background consumer in Q2. That makes it a real module, not a "utility".

### C4 - Drivers accept delivery requests
- **Reason:** the brief explicitly lists "receive delivery requests" and "accept a delivery" as driver capabilities. Restaurant-owned drivers are kept (the earlier decision), so requests go only to that restaurant's on-shift drivers. No matching algorithm is needed, and "first to accept wins" is one optimistic-concurrency update.

### C5 - In-process events added in Q1
- **Reason:** Ordering calls Delivery ("request a driver"). If Delivery then *called* Ordering back ("delivered"), the two modules would depend on each other in a cycle. Publishing `DeliveryCompleted` as an event breaks the cycle. It also prepares Q2, where the same events become durable with no change to the publishing code's intent.

### C6 - Different "2 things deliberately not done"
- **Reason:** the question wants omissions that show *architectural judgement*. Not building microservices/a broker is the most important omission at this stage, and it's the one the Q2/Q3 questions test. Ratings/reviews is a product-feature omission and is still listed under "also not done".

### C7 - RabbitMQ → outbox + PostgreSQL queue
- **Before:** add RabbitMQ for async notifications.
- **Technical reason for the change:**
  1. **Dual-write problem.** "Save order in PostgreSQL, then publish to RabbitMQ" can't be atomic. A crash between the two loses the message. You'd need an outbox *anyway*.
  2. **Once you have an outbox in PostgreSQL**, a queue in PostgreSQL (with `FOR UPDATE SKIP LOCKED`) handles our volume with **no new server**, which matters with no DevOps team.
  3. It sets up Q3 directly: the outbox is what guarantees "payment succeeded" is never lost.
- **When RabbitMQ (or similar) would come back:** many independent consumers, cross-service messaging, or throughput beyond PostgreSQL. See DECISION-LOG D12.

### C8 - Rolling deploys are the main fix for disruptive deployments
- **Reason:** the disruption comes from running *one* instance and making breaking schema changes. Rolling deploys on 2+ instances with health checks, plus expand/contract migrations, remove the outage. Feature flags are good, but they separate *release* from *deploy*. On their own they don't stop the deploy itself being disruptive.

### C9 - Payments stays a module (not extracted)
- **Before:** Q3 extracted Payments into its own service with its own database and RabbitMQ commands.
- **Technical reason for the change:** the Friday incident is caused by an **external** dependency returning ambiguous results. Every fix (attempt ledger, idempotency keys, outbox, circuit breaker, reconciliation) works identically inside a module. Extraction adds a **second network boundary** (Ordering ↔ Payments) that can *also* time out ambiguously, plus another pipeline and database. That is more of the problem we're solving. Isolation is achieved with a dedicated worker queue (bulkhead) + circuit breaker.
- **Kept from the earlier work:** all the patterns and research in `Question3/Research/`. They apply unchanged.
- **When extraction becomes right:** a separate payments team/cadence, PCI isolation requirements, or very different scaling (DECISION-LOG D23).

### C10 - Ambiguity belongs to Payments, not to the order
- **Reason:** Ordering shouldn't need to know *why* payment is pending (timeout? provider down? awaiting webhook?). That's Payments' job. The order stays `PAYMENT_PENDING` until Payments publishes a definite answer. This keeps the order state machine simple and keeps each module's knowledge inside its boundary. Void/refund are payment actions, so their states live in `payment_attempts`.

### C11 - Payment becomes asynchronous, triggered by the order
- **Reason:** in the earlier Q3 the order was created first (good), but the shape of the request was still "wait for payment". Making `PlaceOrder` return `202` after saving `PAYMENT_PENDING` + outbox means web requests never wait on the provider. A single code path then handles both normal operation and recovery.

### C12 - Integration Service → PartnerIntegration module, with scope
- **Reason:** "consistent with our architectural approach" = a modular monolith. An ACL is a *role*, not a deployment unit, so it can be a module with its own schema, slices and adapters. The earlier answer also didn't pin down **what "integrate" means**. The final answer defines Phase 1 (their restaurants orderable on our platform + combined reporting), keeps payments separate per platform, and states who owns which data.

### C13 - New diagrams
| Old diagram | Replaced by (for presenting) | Why |
|-------------|------------------------------|-----|
| `diagrams/06-ArchitectureDiagram.drawio` (layered labels, colours) | `FinalAnswers/Question1/diagrams/Q1-Architecture.drawio` page 1 | Shows vertical slices per module, schemas, public APIs, events, adapters. Plain style. |
| `Question2/diagrams/01-Q2Architecture.drawio` (RabbitMQ) | `FinalAnswers/Question2/diagrams/Q2-Architecture.drawio` page 1 | Same layout as Q1 + tagged additions (Redis, worker, outbox/queue, replica, CDN, CI). |
| `Question2/diagrams/02-AsyncNotificationFlow.drawio` | `Q2-Architecture.drawio` page 2 | Outbox-based flow + cache-aside flow. |
| *(no Q3 diagram existed)* | `FinalAnswers/Question3/diagrams/Q3-Architecture.drawio` pages 1-3 | Architecture, state machines, timeout → recovery sequence. |
| *(none)* | `FinalAnswers/Challenge/diagrams/Challenge-Integration.drawio` | The ACL module and data-ownership split. |
| *(none)* | `Q1-Architecture.drawio` page 2 | Layered vs vertical slices side by side, for explaining C1. |

**Design rules used for all new diagrams:** black on white, no colours, no emojis. Dashed border = new at this stage. Thick border = module changed at this stage. Solid arrow = call/command, dashed arrow = event/async/read-only. **Same coordinates for every module in Q1, Q2 and Q3**, so flipping between the files shows exactly what moved. PNG exports sit next to each `.drawio` file for quick viewing.

`diagrams/01-05` (user flows, system flowchart, ERD) are still valid references apart from the payment step noted in C2 and the Menu → Restaurants merge (C3), which doesn't change the ERD's tables, only which module owns `menu_items`.

### C14 - Old files
A one-line banner was added at the top of `00-ArchitectureSummary.md`, `Question1-Answers.md`, `Question2-Answers.md` and `Question3-Answers.md` pointing to `FinalAnswers/`. Nothing else in them was changed (including your uncommitted edits to `Question3-Answers.md`).
