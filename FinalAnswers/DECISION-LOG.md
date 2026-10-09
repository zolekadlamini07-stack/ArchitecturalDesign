# Decision Log - Architecture & Technology Choices (Q1 → Q2 → Q3 → Challenge)

> **Rule for this project:** no technology or pattern enters the architecture without an entry here.
> Each entry says **what it is**, **what it does for us**, **which other options existed (pros/cons)**, **why this one at this stage**, and **what would make us revisit it**.
> Entries are never deleted. If a later stage changes a decision, the old entry gets a "Superseded/extended by" note, so the evolution stays visible.

## Index

| ID | Stage | Decision | Status |
|----|-------|----------|--------|
| D1 | Q1 | Modular monolith | Active through Q3 |
| D2 | Q1 | Pragmatic vertical slices inside modules | Active |
| D3 | Q1 | Module boundary rules + architecture tests | Extended by D17 |
| D4 | Q1 | PostgreSQL, one database, schema per module | Extended by D14 |
| D5 | Q1 | In-process calls (commands) + in-process events (facts) | Events superseded by D12 |
| D6 | Q1 | Payments: authorise at checkout, capture on accept, provider behind adapter, hosted card fields | Extended by D18-D22 |
| D7 | Q1 | Drivers belong to restaurants; drivers accept delivery requests | Active |
| D8 | Q1 | Polling for status (+ push as nudge) | Active |
| D9 | Q1 | Managed PaaS hosting + managed PostgreSQL | Active |
| D10 | Q1 | One responsive web app (PWA) + push/email provider | Active |
| D11 | Q2 | Redis cache-aside for browse/menu + CDN | Active |
| D12 | Q2 | Transactional outbox + PostgreSQL-backed job queue (no broker) | Active |
| D13 | Q2 | Separate worker process (same codebase) | Extended in Q3 (per-queue concurrency) |
| D14 | Q2 | Read replica for reporting + daily summary table | Active |
| D15 | Q2 | 2+ instances, rolling deploys, CI, expand/contract migrations | Active |
| D16 | Q2 | Feature flags (simple table) | Active |
| D17 | Q2 | Stronger boundaries: DB roles per module, CODEOWNERS, contracts | Extended in Q3 (teams) |
| D18 | Q3 | Order-first; payment asynchronous in worker | Active |
| D19 | Q3 | Payment attempt ledger with UNKNOWN state + append-only events | Active |
| D20 | Q3 | Idempotency keys at every hop | Active |
| D21 | Q3 | Timeouts + retries (backoff + jitter) + circuit breaker + bulkhead | Active |
| D22 | Q3 | Reconciliation: webhooks + sweeper + daily settlement | Active |
| D23 | Q3 | Keep Payments as a module (do not extract yet) | Active |
| D24 | Q3 | Observability: correlation id, structured logs, alerts, support timeline | Active |
| D25 | Q3 | Two teams own modules; city as data | Active |
| D26 | Challenge | Anti-Corruption Layer as a new module | Active |
| D27 | Challenge | Synced read-only copy of partner catalogue (not live calls) | Active |
| D28 | Challenge | Version pinning + consumer-driven contract tests | Active |
| D29 | Challenge | Ownership split: each platform charges for its own orders | Active |

---

# Question 1 decisions

## D1 - Modular monolith
- **What it is:** one deployable application split internally into modules. Each module owns its data, exposes a public API and hides everything else.
- **What it does for us:** one thing to build/deploy/monitor (no DevOps needed), in-process calls (no network failures between modules), while keeping the code shaped so parts can be extracted later.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| Traditional (unstructured) monolith | Fastest start | Turns into a big ball of mud; can't split later |
| **Modular monolith** | Simple ops + strong internal boundaries; evolvable | Needs discipline/enforcement; no independent scaling/deploys |
| Microservices | Independent deploy/scale, team autonomy | Network failures everywhere, many pipelines, distributed debugging, cost: far beyond 3 devs with no DevOps |
| Serverless functions | No servers, pay-per-use | Many small deployables, cold starts, harder local dev, vendor lock-in; the same distribution problems |

- **Why this one now:** 3 developers, no DevOps, tiny budget, unproven business. The biggest risk is building the wrong product, so we optimise for speed of change and low operating cost.
- **Revisit when:** modules need different scaling or release cadences, or teams block each other. (Reviewed in Q2 and Q3: still holds.)

## D2 - Pragmatic Vertical Slice Architecture inside each module
- **What it is:** code inside a module is organised **by use case** (`features/place-order/`, `features/accept-order/`), each slice containing its endpoint, request, handler, data access and tests. "Pragmatic" = we keep a small **shared domain model per module** for real business rules (e.g. the `Order` state machine), and use interfaces only at external systems and module APIs.
- **What it does for us:** a feature change touches one folder; requirements map 1:1 to folders; simple reads stay simple; fewer merge conflicts as the team grows.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| Layered (presentation/application/domain/infrastructure) *(earlier choice)* | Widely known, clear separation | Horizontal: every feature touches all layers; shared big service files become conflict hotspots; ceremony for trivial reads |
| Clean Architecture | Strong dependency rule, very testable | Most ceremony (interfaces, mappers per layer); still horizontal |
| Hexagonal everywhere | Excellent isolation of I/O | Ports for everything is overkill for CRUD |
| **Vertical slices (pragmatic)** | Feature-cohesive, low ceremony, grows by adding folders | Risk of duplication/inconsistency between slices; needs conventions |
| Full DDD tactical patterns everywhere | Rich models for complex domains | Most of our modules are simple; heavy for 3 devs |
| A-Frame | Pure logic separated from I/O, easy tests | Another layering scheme; we only borrow its idea |

- **Why this one now:** matches the modular monolith's *vertical* cutting (modules by capability, slices by use case), the lowest ceremony for 3 devs, and it scales to 5/12 devs with fewer collisions.
- **Revisit when:** a module's rules become complex enough that a richer domain layer is needed. That can be added *inside that module only*.

## D3 - Module boundary rules + architecture tests
- **What it is:** rules (a module may only use another's public API/events, never its internals or tables) checked by an automated test that fails the build. Tools: dependency-cruiser or eslint-plugin-boundaries (TypeScript), ArchUnit (Java), NetArchTest (.NET).
- **What it does for us:** stops the shared codebase from quietly turning into a tangle (Q1 Risk 1).
- **Options:** convention + code review only (cheap, but erodes under pressure) vs **automated tests** (cheap to add, never tired) vs separate repositories/packages per module (strong, but heavy tooling for 3 devs).
- **Why now:** costs an afternoon and protects the main asset of the design. **Extended by D17.**

## D4 - PostgreSQL, one database, one schema per module
- **What it is:** an open-source relational database. Each module gets its own schema (namespace) inside one database.
- **What it does for us:** transactions (orders and money need them), strong consistency, JSON support if needed, and later it doubles as our job queue (D12). One database to pay for and back up.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| **PostgreSQL** | ACID transactions, mature, cheap managed offerings, replicas, `SKIP LOCKED` queues | Vertical scaling limits (far beyond our needs) |
| MySQL | Similar, very common | Fewer features we use later (e.g. partial indexes, which we use in Q3) |
| MongoDB / document DB | Flexible schema | Weaker multi-document transactions for orders/payments; relations (orders↔lines↔history) are relational by nature |
| Database per module | Strongest isolation | N databases to run and pay for: microservices cost without microservices benefits |

- **Why now:** relational data, money, small team, budget. Schema-per-module gives ownership without extra servers.
- **Revisit when:** a module's load or data needs genuinely diverge (then give *that* module its own database).

## D5 - In-process calls for commands, in-process events for facts
- **What it is:** a module asks another to *do* something by calling its public API (a method call in the same process). When something *has happened*, the owner publishes a domain event to an in-memory dispatcher and subscribers react.
- **What it does for us:** no broker, no network. Events break circular dependencies (Ordering → Delivery for "request a driver", Delivery → `DeliveryCompleted` event → Ordering).
- **Options:** direct calls only (simplest, but creates cycles), **calls + in-memory events** (chosen), message broker (durable, but a server to run: rejected for Q1).
- **Weakness accepted:** in-memory events are lost on crash. **Superseded in Q2 by D12** (durable outbox events).

## D6 - Payments: authorise → capture/void, provider behind an adapter, hosted card fields
- **What it is:** at checkout we **authorise** (hold) the amount; on restaurant **accept** we **capture**; on **reject** we **void**. The provider (e.g. **Stripe**) is reached only through our `PaymentProvider` port's adapter in the Payments module. Card entry uses the provider's hosted fields (Stripe Elements), so card numbers never reach us.
- **What it does for us:** the customer never pays for an order the restaurant rejects, there are no refunds for rejections, we know the card is good before bothering the restaurant, PCI scope stays minimal, and the provider is swappable.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| Charge immediately at checkout | Simplest | Every rejection needs a refund (slow, may cost fees) |
| Charge after restaurant accepts *(earlier choice)* | No refunds | Restaurant may start cooking before we know the card works; customer may have left when the charge fails |
| **Authorise at checkout, capture on accept** | Best of both; void is free and instant | Slightly more states; authorisations expire (~7 days, irrelevant for food) |
| Cash on delivery | No provider | Fraud, driver cash handling, brief says "pay for the order" in the app |

  Provider options: **Stripe** (excellent docs, idempotency keys, webhooks, manual capture, hosted fields), Adyen (enterprise-oriented), PayPal/Braintree, or a regional provider (e.g. Paystack/PayFast in South Africa). Choose the one that supports **manual capture + idempotency keys + webhooks + hosted fields** in your country. Stripe is used as the example throughout.
- **Revisit:** extended in Q3 by D18-D22 (failure handling).

## D7 - Drivers belong to restaurants; drivers accept delivery requests
- **What it is:** each driver is linked to one restaurant (restaurant onboards them). When an order is ready, Delivery creates a **delivery request** shown to that restaurant's **on-shift** drivers, and the first to **accept** gets it.
- **Options:** restaurant manually assigns + driver acknowledges *(earlier choice; the brief requires drivers to "accept")*, **request to on-shift drivers + first accept wins** (chosen), platform-wide gig pool with matching/dispatch (complex, not needed for 1 restaurant).
- **Why now:** matches the brief's verbs (receive, accept), and needs no matching algorithm.
- **Revisit when:** the business pools drivers across restaurants. Only Delivery's targeting rule changes.

## D8 - Polling for status updates (push notifications as nudges)
- **What it is:** clients re-request order/queue status every ~15 s (faster on the "confirming payment" screen in Q3). Push notifications alert users but are never the source of truth.
- **Options:** **polling** (zero infrastructure), Server-Sent Events, WebSockets (real-time but needs connection handling/backplane when scaled), managed real-time services (Pusher/Ably: cost + another vendor).
- **Why now:** trivial load, nothing to operate, and robust (a missed push loses nothing).
- **Revisit when:** real-time tracking/GPS becomes a requirement or polling load becomes significant.

## D9 - Managed PaaS hosting + managed PostgreSQL
- **What it is:** a platform that runs our app from a git push or a container (examples: Render, Railway, Heroku, Fly.io, Azure App Service, AWS App Runner/Elastic Beanstalk) plus a provider-managed PostgreSQL with automated backups.
- **What it does for us:** no servers to patch, built-in TLS, logs, health checks, restarts, scaling sliders, rolling deploys (used in Q2). It replaces the DevOps team we don't have.
- **Options:** **PaaS** (chosen), raw VMs (cheap but we'd be the ops team), Kubernetes (powerful, needs DevOps skills), serverless (see D1).
- **Revisit when:** costs at scale or needs exceed what the platform offers.

## D10 - One responsive web app (installable PWA) + push/email provider
- **What it is:** a single web front-end with customer, restaurant, driver and admin views, installable on phones as a Progressive Web App. Notifications via a push service (e.g. Firebase Cloud Messaging / Web Push) and an email API.
- **Options:** native iOS + Android apps (best UX, but 2 more codebases + app store releases for 3 devs), cross-platform (React Native/Flutter: one more codebase), **responsive web/PWA** (one codebase).
- **Why now:** proves the business with the least code. The REST API means native apps can be added later without backend changes.

---

# Question 2 decisions

## D11 - Redis cache-aside for browse/menu + CDN for static files
- **What it is:** Redis is an in-memory key-value store (managed options: Upstash, Redis Cloud, AWS ElastiCache, Azure Cache). Cache-aside: read from cache, on a miss read the DB and fill the cache (5-min TTL), and delete keys when menus change. A CDN serves JS/CSS/images from edge locations.
- **What it does for us:** takes the most-trafficked, least-changing reads (P1) off PostgreSQL.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| In-process memory cache | Free, fastest | Each of our 2+ instances has its own copy; invalidation can't reach all instances |
| **Redis (managed)** | Shared across instances, simple invalidation, cheap managed tiers | One more managed dependency (if it's down we fall back to the DB) |
| Memcached | Simple, fast | Fewer features; Redis is the more common default |
| HTTP caching / CDN for API responses | Very cheap at scale | Invalidation is coarse; personalised responses complicate it |
| PostgreSQL materialised views | No new tech | Still hits the DB on every request |

- **Why now:** P1 is specifically about read traffic on rarely-changing data, and we just moved to multiple instances. Prices for orders are still read from the DB (safety rule).
- **Revisit when:** cache hit rate is low, or we need per-city/per-user caching at a much larger scale.

## D12 - Transactional outbox + PostgreSQL-backed job queue (no message broker)
- **What it is:** events are written to an `outbox` table in the same DB transaction as the business change. A dispatcher turns them into jobs in a queue that also lives in PostgreSQL (libraries: **pg-boss** for Node, **Hangfire** for .NET, River for Go, or a hand-rolled `FOR UPDATE SKIP LOCKED` loop). Jobs have retries, backoff and dead-lettering.
- **What it does for us:** notifications (P3) and cross-module reactions leave the request path, become **crash-safe** (no lost events) and **retryable**, with zero new servers.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| Fire-and-forget async in the API process | Trivial | Lost on crash/deploy; no retries; still uses API resources |
| **Outbox + PostgreSQL job queue** | No new infra; **atomic with our data**; retries/DLQ; enough throughput | Adds DB load (small at our volume); fan-out/routing done in code |
| Redis-based queue (BullMQ, Sidekiq) | Fast, popular | Not transactional with PostgreSQL → dual-write problem; Redis persistence must be configured carefully |
| RabbitMQ *(earlier choice)* | Mature broker, routing, management UI | Another stateful server; **still needs an outbox** to avoid dual writes; more than our volume requires |
| Kafka / cloud event streams | Huge throughput, replay | Heavy to run, overkill |
| Cloud queues (SQS, Azure Service Bus) | Fully managed | Still dual-write → outbox needed; vendor-specific |

- **Why now:** solves P3 *and* fixes Q1's lost-event weakness (D5), with the fewest moving parts. It also sets up Q3, where the outbox is what guarantees a successful payment is never forgotten.
- **Revisit when:** queue latency rises under load, many independent consumers appear, or we integrate with systems that want a broker.

## D13 - Separate worker process (same codebase)
- **What it is:** the same application started with a different command (`worker` instead of `api`) that only runs jobs. Same release, separate instance.
- **What it does for us:** slow/background work can't steal API capacity (a bulkhead). It scales independently.
- **Options:** run jobs inside API instances (simpler, but competes with users), **separate worker process** (chosen), separate worker *service* with its own codebase (unnecessary split).
- **Extended in Q3:** separate queues (`payments`, `notifications`, `integration`, `reporting`) with their own concurrency limits.

## D14 - PostgreSQL read replica for Reporting + daily summary table
- **What it is:** a managed, continuously updated read-only copy of the database. Only the Reporting module connects to it. A nightly job writes `daily_summary` rows.
- **What it does for us:** heavy report queries stop competing with order traffic (P4).
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| Run reports off-peak only | Free | Business wants reports during the day |
| Materialised views on the primary | No new infra | Refreshes still load the primary |
| **Read replica** (+ summary table) | One connection-string change; isolates load | Seconds of lag (fine for reports); extra DB cost |
| Separate reporting DB fed by events (CQRS read model) | Tailored models | Much more code to build and keep in sync |
| Data warehouse (BigQuery, Snowflake, Redshift) + ETL | Best for analytics | Cost, pipelines, skills: solving problems we don't have |

- **Revisit when:** reporting needs go beyond SQL (cross-system analytics, data science) → warehouse.

## D15 - 2+ instances, rolling deploys, CI pipeline, expand/contract migrations
- **What it is:** run at least two API instances behind the PaaS load balancer. Deploy by replacing instances one at a time after health checks. Run tests + architecture tests in CI (e.g. GitHub Actions) on every PR. Make DB changes in backward-compatible steps.
- **What it does for us:** deploys stop being outages (P5).
- **Options:** rolling (built-in, chosen), blue-green (instant rollback, double cost during switch), canary (needs traffic-splitting), **independent deployment via services** (rejected: the cause is *how* we deploy, not *what*).
- **Revisit when:** we need instant rollback at scale → blue-green.

## D16 - Feature flags (simple table)
- **What it is:** `if (flags.isOn('x', restaurantId))`: code can be deployed switched off and enabled per restaurant or globally.
- **Options:** **own `feature_flags` table** (free, enough), managed service (LaunchDarkly, Unleash, Flagsmith: richer targeting, cost), long-lived branches (merge pain; rejected).
- **Rule:** remove flags once fully rolled out.

## D17 - Stronger boundaries: DB roles per module, CODEOWNERS, module contracts
- **What it is:** each module connects to PostgreSQL with its own role, allowed to write only its own schema (Reporting: read-only). `CODEOWNERS` gives each module an owner who reviews changes. Public APIs/events change only additively.
- **What it does for us:** P6, with ownership enforced by the database and the repository, not just good intentions.
- **Extended in Q3:** module ownership by **teams**, plus contract tests.

---

# Question 3 decisions

## D18 - Order first; payment requested asynchronously in the worker
- **What it is:** `PlaceOrder` saves the order as `PAYMENT_PENDING` + an `OrderPlaced` outbox event, then returns `202 Accepted`. Payments reacts in the worker and calls the provider. The client shows "Confirming your payment..." and polls.
- **What it does for us:** a payment request can't exist without an order ("charged but no order" becomes impossible), and web requests never wait on the provider.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| Synchronous authorise inside `PlaceOrder` *(Q1/Q2)* | Instant answer on the happy path | Provider slowness = our slowness; timeouts mid-request leave ambiguous state |
| Synchronous with short timeout, then fall back to async | Fast happy path | Two code paths for the same thing; harder to reason about |
| **Always async (order first)** | One code path for normal and recovery; isolates the provider | Customer waits ~1-2 s on a "confirming" screen |
| Payment first, then create order | Simple mental model | Exactly the "charged but no order" failure |

## D19 - Payment attempt ledger with an explicit UNKNOWN state + append-only events
- **What it is:** `payment_attempts` (one row per attempt, status `REQUESTED → IN_FLIGHT → AUTHORISED/DECLINED/UNKNOWN → CAPTURED/VOIDED/REFUNDED`), written **before** calling the provider, plus `payment_events`, an insert-only log of every request, response, timeout and webhook.
- **What it does for us:** we always know what we asked and what we heard. Timeouts are recorded honestly as UNKNOWN. A full audit trail exists for support and finance.
- **Options:** store only a status on the order (*Q1/Q2*: loses history, conflates order and payment), **ledger + event log** (chosen), full event sourcing (more machinery than needed).

## D20 - Idempotency keys at every hop
- **What it is:** (1) the client sends an `Idempotency-Key` per checkout to `POST /orders` (unique in `place_order_requests`); (2) one live attempt per order (DB unique) + state-checking handlers + inbox; (3) our `attempt_id` sent to the provider as its idempotency key; (4) partial unique index: one `PAYMENT_PENDING` order per customer.
- **What it does for us:** double clicks, redelivered jobs, duplicate webhooks and retries can't create duplicate orders or charges.
- **Options:** disable the button in the UI only (helps, but doesn't survive refreshes, retries or redeliveries), dedupe by "same basket within N minutes" heuristics (fragile), **keys + DB constraints** (chosen, enforced by the database).

## D21 - Timeouts, retries with backoff + jitter, circuit breaker, bulkhead
- **What it is:** 10 s timeout on provider calls; automatic retries only for transient errors, with the same key and growing randomised waits; a circuit breaker (CLOSED/OPEN/HALF-OPEN) around the provider; payment jobs on their own queue with a concurrency cap. Libraries: **opossum** (Node), **Polly** (.NET), **Resilience4j** (Java).
- **What it does for us:** a failing provider is contained to delayed payment confirmations. The system stops hammering it and recovers automatically when it's healthy.
- **Options:** retries only (amplifies outages), timeouts only (still wastes capacity during outages), **the combination** (chosen), a service mesh doing this at network level (needs Kubernetes/DevOps: rejected).

## D22 - Reconciliation in three layers
- **What it is:** (1) provider **webhooks** into an inbox (signature-verified, de-duplicated); (2) a **sweeper** job every minute for UNKNOWN/stale attempts (same-key replay or lookup by our reference); (3) a **daily settlement** comparison of the provider report against our ledger, with mismatches going to a support exception queue.
- **What it does for us:** our records converge to the provider's truth automatically. "How do we reconcile?" and "How do we know what happened?" are answered.
- **Options:** webhooks only (can be missed), polling only (slower, more calls), manual dashboard checks (*Q1*, impossible at scale), **all three** (chosen: standard practice for money).

## D23 - Keep Payments as a module (do not extract to a microservice yet)
- **What it is:** a decision *not* to change topology during the incident response.
- **Options considered:**

| Option | Pros | Cons |
|--------|------|------|
| **Keep as module + worker queue + circuit breaker** | Isolation achieved without a network hop; one deployable | Payments deploys with everything else |
| Extract Payments service *(earlier answer)* | Independent deploy/scale, separate team ownership, PCI isolation | Adds a network hop between Ordering and Payments (more ambiguity), a second pipeline and a second database to run; doesn't fix the provider-ambiguity problem itself |

- **Why:** the incident is about an *external* dependency. Every fix (ledger, keys, outbox, breaker, reconciliation) works the same inside the module. Extraction would be a separate, team-driven decision.
- **Revisit when:** the payments team needs its own release cadence, compliance demands isolation, or load differs greatly. Payments is already extraction-ready (own schema, events only).

## D24 - Observability: correlation id, structured logs, alerts, support timeline
- **What it is:** order id as correlation id in every log line/job/provider metadata; JSON logs; metrics + alerts (circuit OPEN, UNKNOWN count, oldest pending order, DLQ size, provider p95 latency) using the PaaS tooling or a hosted tool (Grafana Cloud, Datadog, Sentry); a `GetOrderTimeline` support slice.
- **What it does for us:** support can answer "did I pay?" in seconds, and engineers can reconstruct incidents.
- **Options:** plain text logs (unsearchable at scale), full distributed tracing with OpenTelemetry (useful later; one process doesn't need it yet), **structured logs + key alerts + timeline** (chosen).

## D25 - Two teams own modules; city as data
- **What it is:** Team *Ordering & Payments* (Identity, Customers, Ordering, Payments, Notifications) and Team *Restaurants & Delivery* (Restaurants, Delivery, Reporting), with contract tests on module APIs. `city_id` on restaurants, with cache keys/reports per city, UTC timestamps and currency-aware `Money`.
- **What it does for us:** 12 devs with clear ownership (Conway's law used on purpose). Adding 10 cities is configuration, not code.
- **Options:** teams by layer (frontend/backend: every feature needs both teams, rejected), teams by city (duplicated knowledge, rejected), **teams by business capability** (chosen). Per-city deployments/databases (premature; rejected).

---

# Challenge decisions

## D26 - Anti-Corruption Layer as a new module (`PartnerIntegration`)
- **What it is:** a module that is the only code aware of the acquired platform. It has adapters to their APIs, translation between models, an ID-mapping table, and an inbox/outbox. It talks to our other modules only via their public APIs/events.
- **Options:** shared database (rejected: couples schemas and breaks ownership), point-to-point calls from each of our modules (their model leaks everywhere), **ACL module** (chosen), separate integration microservice (same logic + network hop; not needed yet), ESB/iPaaS (cost, logic outside our codebase), full migration (excluded by the brief).
- **Why:** consistent with our architecture (it's just another module), isolates their model and failures, and keeps a strangler-fig migration possible later.

## D27 - Synced read-only copy of the partner catalogue (not live calls)
- **What it is:** a periodic + webhook-triggered sync that copies their restaurants/menus into our Restaurants module, marked `source = PARTNER`.
- **Options:** live API calls on every browse (their latency/outages become ours), **synced copy** (chosen: fast, resilient, cacheable, and their system remains the owner).
- **Trade-off:** minutes of staleness. Orders are still validated by them when forwarded, and a stale sync (>30 min) marks their restaurants unavailable.

## D28 - Version pinning + consumer-driven contract tests
- **What it is:** pin their API version in the adapter; publish our expectations as **Pact** contracts that their CI runs (or nightly checks against their sandbox); tolerant reader parsing; a separate versioned `/partner/v1` API if they ever call us.
- **Options:** "just integrate and watch for errors" (breaks in production), end-to-end test environment spanning both companies (slow, brittle), **contract tests** (chosen: fast feedback at the boundary).

## D29 - Each platform charges for its own orders; no payment-system integration
- **What it is:** orders placed on our platform are paid through our Payments module (Q3 flow); orders on theirs are paid through theirs. Money between the companies is settled by finance.
- **Options:** route all payments through one system (a rewrite of one side, rejected by the brief), **keep separate** (chosen).
- **Why:** avoids touching the riskiest part of either system, and our Q3 payment guarantees apply unchanged to partner-restaurant orders.
