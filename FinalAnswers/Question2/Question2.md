# Question 2 - Six Months Later: Review and Evolve

> **Stage:** 15 restaurants (50 expected within a year), more drivers, a much larger customer base, still one city, 5 developers, more money, still no dedicated DevOps.
>
> **One-line answer:** **Keep** the modular monolith and the vertical slices. They are not the problem. **Add** targeted pieces that each fix one observed problem: a cache for browsing, a **durable background job queue in PostgreSQL** (via an outbox) run by a separate **worker process** for notifications, a **read replica** for reporting, **zero-downtime rolling deploys** on 2+ instances, and **enforced** module ownership. **Nothing is added that isn't tied to a symptom in the brief.**
>
> Diagram: [`diagrams/Q2-Architecture.drawio`](diagrams/Q2-Architecture.drawio). Page 1 is the architecture with every change labelled `[NEW Q2]` / `[CHANGED Q2]`, and page 2 is the new "place order → notify" flow.
> Decisions and alternatives: [`../DECISION-LOG.md`](../DECISION-LOG.md) entries **D11-D17**. Concepts: [`Research.md`](Research.md).

---

## What changed in the context (Q1 → Q2)

| | Q1 | Q2 | Why it matters |
|---|---|---|---|
| Restaurants | 1 | 15 → 50 | Browsing and menu reads multiply. Orders per minute multiply. |
| Customers | small | significantly larger | Read traffic and data volume grow. |
| Developers | 3 | 5 | More people in the same code at once. |
| Budget | very limited | more to invest | We can now afford a managed cache, a replica and a second instance. |
| DevOps | none | still none | **Anything new must be managed/hosted, not self-run.** |
| Cities | 1 | 1 | **No multi-region or multi-city work.** |

---

# Identify

## 1. What is now becoming a problem?

Each symptom in the brief, traced to the **Q1 decision that causes it**:

| # | Symptom (from the brief) | Root cause in the Q1 architecture |
|---|--------------------------|-----------------------------------|
| P1 | Restaurant browsing gets far more traffic than everything else | Every browse/menu view is a database query. Read-heavy, rarely-changing data is fetched fresh every time, by everyone, from the same database the orders use. |
| P2 | Some database operations are becoming expensive | Data volume is roughly 15x. Queries that were fine on small tables now scan (missing indexes, unpaginated history lists, N+1 queries in a few slices). |
| P3 | Notifications are slowing down certain operations | In Q1, `Notify` is a **synchronous in-process call** inside the request. `PlaceOrder`, `AcceptOrder` and others wait 100-500 ms+ for the push/email provider, and longer when it's slow. |
| P4 | Reporting queries affect normal performance | Reporting runs heavy aggregate SQL (SUM/COUNT/GROUP BY over months of orders) **on the same database** that is taking orders. |
| P5 | Deployments are becoming more disruptive | **One instance**: a deploy restarts the only copy of the app, so every deploy is a short outage. Schema changes are applied in a way that briefly breaks the running version. |
| P6 | Developers increasingly work in the same areas | 5 devs. The Q1 architecture test checks code imports, but **the database is not protected**: every module connects with the same credentials, so SQL "shortcuts" into other schemas creep in (risk R1 from Q1 starting to happen). **Nobody clearly owns each module**, and shared files (`shared/`, module public APIs) become hotspots. |
| P7 | Business wants to add restaurants rapidly (50 within a year) | Restaurant onboarding is a **manual, developer-assisted** step (admin creates accounts by hand). |

## 2. Which parts of the original architecture still work?

| Q1 element | Verdict | Why it still works |
|------------|---------|--------------------|
| **Modular monolith** (one deployable) | **Keep** | 5 devs is still small for microservices. None of P1-P7 is caused by "being one deployable"; each has a cheaper fix. |
| **Vertical slices inside modules** | **Keep, and it's now paying off** | New features (P7 onboarding) are *new folders*. Slices let 5 people work on different features without editing the same layer files. The collisions in P6 come from *missing ownership/enforcement*, not from the structure. |
| **8 modules / module boundaries** | **Keep** | Still the right business split. Clean boundaries are *why* each change below is local to one or two modules. |
| **Schema per module, single PostgreSQL** | **Keep** (one primary) | PostgreSQL easily handles 50 restaurants in one city once indexed. We add a *replica*, not more databases. |
| **Ports & adapters for external systems** | **Keep** | Moving notifications to the background touches only the Notifications module; the adapter is unchanged. |
| **Authorise → capture/void payment flow** | **Keep** | Not mentioned as a problem. Changing it would be redesigning "because we can". |
| **Drivers belong to restaurants** | **Keep** | Still true. If the business ever pools drivers, only Delivery's "who sees this request" rule changes, which is exactly the benefit of the boundary. |
| **Polling for order status** | **Keep** | Still cheap, and it gets cheaper with cache-friendly endpoints. Not a reported problem. |

## 3. Which parts need to change?

Every change answers **one** problem. Implementation order is at the end.

### Change 1 - Cache browsing and menus (fixes P1, helps P2)
- **What:**
  1. A **managed Redis** cache (D11). The `BrowseRestaurants` and `GetMenu` slices use **cache-aside**: look in Redis first; on a miss, read PostgreSQL and store the result for **5 minutes**.
  2. **Invalidation in the writing slices**: `UpdateMenuItem`, `SetItemAvailability`, `SetOpenStatus` etc. delete the affected keys *after* their transaction commits. The 5-minute expiry is the safety net if a delete is ever missed.
  3. **HTTP caching + CDN** for the web app's static files (JS/CSS/images), which most PaaS platforms include.
- **Why Redis rather than in-memory:** we now run **2+ app instances** (Change 5). An in-memory cache per instance would give different customers different menus after an update, and "delete key" would only clear one instance's copy. A shared cache has one copy and one invalidation.
- **Safety rule:** the cache is used **only for display**. `PlaceOrder` still prices from the database (`Restaurants.getPricedItems`), so a stale cache can never make us charge the wrong price or sell an unavailable item.
- **Modules touched:** Restaurants only (its read slices plus its write slices).

### Change 2 - Background processing: durable events + job queue + worker process (fixes P3)
- **What:**
  1. **Transactional outbox.** When a slice saves its change (e.g. `PlaceOrder`), it writes the event (`OrderPlaced`) into an `outbox` table **in the same database transaction**. Either both are saved or neither is.
  2. **PostgreSQL-backed job queue** (D12, e.g. pg-boss for Node, Hangfire for .NET, or a small `SELECT ... FOR UPDATE SKIP LOCKED` loop). A dispatcher turns outbox rows into jobs, one per subscriber (`Notifications.NotifyRestaurantOfNewOrder`, ...).
  3. **A separate worker process** (D13) runs those jobs. It is the **same codebase** with a different start command (`node worker.js` vs `node api.js`). It has the same modules and the same slices and is deployed in the same release, but it runs on its own instance, so slow jobs never steal capacity from customer requests.
  4. **Retries with backoff** (e.g. 5 attempts: 10 s, 30 s, 2 min, ...) and a **dead-letter state** for jobs that keep failing, visible on an admin screen.
  5. **Idempotent handlers**: jobs can run more than once (at-least-once delivery), so each handler checks first. For example `notification_log` has a unique key on `(event_id, recipient)`, so a retried job doesn't send the push twice.
- **Effect:** `PlaceOrder` now = validate → price → authorise payment → save order + outbox row → **return**. The push to the restaurant happens a few hundred milliseconds later in the worker, and a slow push provider no longer slows any user.
- **This also upgrades Q1's in-process events to durable events.** `DeliveryCompleted` → Ordering now survives a crash too.
- **Why not RabbitMQ / a message broker?** It's another stateful server with no DevOps team. It also creates a *dual-write problem* (save the order in PostgreSQL, then publish to RabbitMQ: if we crash in between, the notification is lost forever), so you would *still* need an outbox in front of it. At our volume (thousands of jobs per hour, not per second) PostgreSQL handles the queue comfortably. Full comparison in D12.

### Change 3 - Separate reporting from the transactional database (fixes P4)
- **What:** a **managed PostgreSQL read replica** (D14), a continuously updated read-only copy. The **Reporting module's connection points at the replica**. Every other module stays on the primary.
- **Plus:** a nightly worker job fills a small `reporting.daily_summary` table (orders, revenue, deliveries per restaurant per day), so dashboards read a few hundred pre-computed rows instead of scanning months of orders. That summary is computed on the primary at 03:00 when load is lowest and is read from the replica.
- **Trade-off accepted:** the replica lags by a few seconds. That's fine for business reports, and the brief asks for "basic reporting", not real-time.
- **Why not a data warehouse / separate reporting store / CQRS?** Those are the right answer to "we need complex analytics across systems". Our problem is narrower: *reports are slowing orders*. The replica removes the contention with almost no code change (one connection string). Revisit if reporting needs grow beyond SQL on a replica.
- **Modules touched:** Reporting only.

### Change 4 - Fix the expensive queries (fixes P2)
This one is not new technology. It's engineering hygiene, done **with measurements**:
1. Turn on `pg_stat_statements` (built into managed PostgreSQL) to find the top 10 slowest/most frequent queries. **Fix what the data says, not what we guess.**
2. Add the indexes those queries need. Likely candidates: `ordering.orders (restaurant_id, status)`, `(customer_id, created_at DESC)`, `delivery.deliveries (driver_id, created_at DESC)`, `restaurants.menu_items (restaurant_id, is_available)`.
3. **Paginate** history slices (`ListMyOrders`, `ListRestaurantOrderHistory`, `ListMyDeliveries`).
4. Fix N+1 queries inside slices. In VSA each slice owns its query, so this is a local fix.
5. If still needed, move up one database tier (more money is available).

### Change 5 - Zero-downtime deployments (fixes P5)
- **Run 2+ app instances** behind the platform's load balancer, with a `/health` endpoint.
- **Rolling deploys** (D15): the platform starts the new version, waits until it's healthy, moves traffic, then stops the old one. Users see no outage.
- **Backward-compatible database migrations ("expand → migrate → contract")**: e.g. to rename a column, first *add* the new one (old and new code both work), deploy, backfill, and only *later* drop the old one. This is required, because during a rolling deploy old and new code run at the same time.
- **CI pipeline** (e.g. GitHub Actions) that runs tests + architecture tests on every pull request, then deploys automatically. No more manual deploys.
- **Feature flags** (D16) for risky features: deploy the code switched off, then switch it on for one restaurant, then all. *Deploying* stops being the same as *releasing*.
- **Why not independent deployment (separate services)?** Disruption comes from *how* we deploy (one instance, breaking migrations), not from deploying one thing. Rolling deploys fix that without the cost of microservices.

### Change 6 - Enforce module boundaries and ownership (fixes P6)
- **Architecture tests run on every pull request in CI** (in Q1 they ran locally/in the build). Illegal cross-module imports fail the PR before review.
- **Separate database roles per module** (`ordering_rw` can only write to the `ordering` schema). Ownership becomes enforced by PostgreSQL itself, not only by convention. The Reporting role is read-only.
- **Module ownership** via a `CODEOWNERS` file: each module has a primary owner who reviews changes to it, and a change to a module's **public API** needs that owner's review.
- **Module public APIs and events are treated as contracts**: changes are additive, and a field is never removed without notice.
- **VSA reduces the remaining conflicts by design**: two developers working on two features work in two different `features/` folders.

### Change 7 - Self-service restaurant onboarding (supports P7)
- New slices in existing modules (no new architecture): `ApplyAsRestaurant` (Restaurants), `ApproveRestaurant` (admin, Restaurants), which raises `RestaurantApproved` → Identity creates the staff account (via the job queue). The restaurant builds its own menu with the existing slices.
- `BrowseRestaurants` gets **simple filtering + pagination** (cuisine, open now, name search with a PostgreSQL index). **No search engine** for 50 restaurants in one city.

## 4. Which parts should deliberately remain simple?

| Kept simple | Why | What would change our mind |
|-------------|-----|----------------------------|
| **One deployable (no microservices)** | 5 devs, no DevOps. Every Q2 problem has a cheaper fix. | Teams blocking each other's releases, or modules needing very different scaling. |
| **One primary PostgreSQL** (+ a replica, not more databases) | Comfortably handles 50 restaurants in one city. One thing to back up. | Write load the primary can't handle after tuning. |
| **No message broker** (queue lives in PostgreSQL) | No new server to run, and the queue is transactional with our data for free. | Event volume or fan-out that strains PostgreSQL, or a need to integrate external consumers. |
| **No Kubernetes / containers to manage** | PaaS does rolling deploys and scaling for us. | Needing infrastructure the PaaS can't provide. |
| **Polling for order tracking** | Still cheap and robust. | Real-time tracking demands (GPS, Q1 Not-done 2). |
| **No search engine** (Elasticsearch etc.) | 50 restaurants fit in one indexed SQL query. | Thousands of restaurants, fuzzy/geo search. |
| **Payment flow unchanged** | Not reported as a problem. | Payment failures, which is what happens in Q3. |
| **No multi-city / multi-region** | Still one city. | Expansion, which is Q3. |

## 5. What architectural decisions would you reconsider?

| Q1 decision | Reconsidered? | New decision | Reason |
|-------------|---------------|--------------|--------|
| Notifications as synchronous in-process calls | **Yes** | Durable events → job queue → worker | Directly causes P3. |
| In-process (in-memory) domain events | **Yes** | Transactional outbox + job queue | Same fix, and it makes cross-module reactions crash-safe and retryable. |
| Reporting reads the live operational database | **Yes** | Read replica (+ daily summary table) | Directly causes P4. |
| No caching | **Yes** | Redis cache-aside for browse/menu | Directly causes P1. |
| Single instance, manual deploy | **Yes** | 2+ instances, rolling deploys, CI, expand/contract migrations, feature flags | Directly causes P5. |
| Module boundaries enforced by code-level architecture test + review | **Yes, strengthened** | + DB roles per module + architecture tests on every PR + CODEOWNERS | P6 shows code-level checks alone aren't enough at 5 devs: the database must enforce ownership too. |
| Modular monolith | Reviewed, **kept** | - | The problems aren't caused by it. |
| Vertical slices | Reviewed, **kept** | - | They reduce P6 and make each change local. |
| Payment: synchronous authorise in `PlaceOrder` | Reviewed, **kept for now** | - | Not a reported problem. Flagged as the next risk (Q1 R2). |

---

# Questions to consider - should we introduce...?

| Option | Introduce? | What problem it solves (or why not) |
|--------|-----------|--------------------------------------|
| **Caching?** | **Yes**, Redis cache-aside on browse/menu + CDN for static files | P1 (and part of P2): read-heavy, rarely-changing data. |
| **Background processing?** | **Yes**, a worker process | P3: slow work leaves the request path. |
| **Messaging?** | **Yes, as durable events (outbox)**, but **no broker** | P3 + makes cross-module events crash-safe. A broker adds a server and a dual-write problem without solving anything extra at our scale. |
| **Queues?** | **Yes, a PostgreSQL-backed job queue** | Retries, backoff and dead-lettering for background work without new infrastructure. |
| **Separate reporting storage?** | **Partly**: a read replica + a small summary table, not a warehouse | P4: removes contention. A warehouse solves analytics needs we don't have. |
| **Independent deployment?** | **No** | P5 is solved by rolling deploys. Independent deployment costs microservices-level complexity. |
| **Additional services?** | **No new services.** One new *process* (the worker) from the same codebase | The worker isolates slow work. A new service would add network calls and a second data owner for no gain. |
| **Stronger module boundaries?** | **Yes** | P6: CI architecture tests, DB roles, CODEOWNERS, module APIs as contracts. |

## Implementation order (cheapest/biggest win first)

| # | Change | Effort | Why this order |
|---|--------|--------|----------------|
| 1 | Measure + indexes + pagination (Change 4) | Low | No new infrastructure. Might shrink P1/P4 too. |
| 2 | Background processing (Change 2) | Medium | Removes user-visible latency. Also provides the worker needed by later changes. |
| 3 | 2 instances + rolling deploys + CI (Change 5) | Low-Medium | Mostly platform settings. Needed before a shared cache makes sense. |
| 4 | Redis cache (Change 1) | Medium | Biggest relief for the busiest path. |
| 5 | Read replica for Reporting (Change 3) | Low | One connection string + a nightly job. |
| 6 | Enforcement & ownership (Change 6) | Low | Process + tests. |
| 7 | Self-service onboarding (Change 7) | Medium | A business feature; ready before restaurant growth. |

## Q1 → Q2 at a glance

| Aspect | Q1 | Q2 |
|--------|----|----|
| Deployment topology | Modular monolith | Modular monolith **(unchanged)** |
| Internal structure | Pragmatic vertical slices | Pragmatic vertical slices **(unchanged)** |
| Runtime processes | 1 instance (API) | **2+ API instances + 1 worker** (same codebase) |
| Database | 1 PostgreSQL | **Primary + read replica** (Reporting only) |
| Caching | None | **Redis** for browse/menu, **CDN** for static files |
| Cross-module events | In-memory | **Durable: outbox + PostgreSQL job queue** |
| Notifications | Synchronous, in request | **Asynchronous, in worker, with retries** |
| Deployment | Manual, single instance | **CI + rolling, expand/contract migrations, feature flags** |
| Boundary enforcement | Architecture test (code imports) + review | **+ DB roles per module + tests on every PR + CODEOWNERS** |
