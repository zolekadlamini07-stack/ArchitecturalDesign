# Question 2 - Research Notes (concepts behind the answer)

> Deeper background from earlier work: `../../Question2/Research/01-CachingStrategies.md`, `02-ReportingSeparation.md`, `03-DeploymentStrategies.md`, `04-MessageQueues.md`.
> **One difference:** that earlier research picked **RabbitMQ**. The final answer uses a **PostgreSQL-backed queue + transactional outbox** instead. Reasoning in section 3 below, DECISION-LOG D12 and `../CHANGES-AND-REASONS.md` C6.

---

## 1. Caching: cache-aside and invalidation

**Cache-aside (lazy loading).** The application, not the cache, is in charge:
```
read:   value = cache.get(key)
        if miss: value = db.query(...); cache.set(key, value, ttl = 5 min)
        return value
write:  db.save(...); commit; cache.delete(key)     // next read re-fills it
```

| Strategy | How | Good for | Downside |
|----------|-----|----------|----------|
| **Cache-aside** (chosen) | App fills on miss, deletes on write | Read-heavy data, simple to reason about | First read after a change is slow (a miss) |
| Write-through | App writes to cache and DB together | Always-warm cache | Every write slower; caches data nobody reads |
| Write-behind | Write to cache, flush to DB later | Very high write rates | Risk of data loss; complex |
| Read-through | Cache library loads from DB itself | Same as cache-aside, hidden | Less control |

**Invalidation: "the hard problem".** We combine:
- **explicit delete** after commit (fresh within milliseconds normally), and
- **TTL (time-to-live) of 5 min** as the safety net if a delete is ever lost.
- **Never trust the cache for money.** Prices for an order are always read from the database.

**Why a shared cache (Redis) instead of in-process memory:** with 2+ instances, each instance would have its own copy and you can't delete a key on "all instances" without extra machinery. Redis = one copy, one delete.

**Key design:** `restaurants:list:{filtersHash}`, `menu:{restaurantId}`. Deleting `menu:{id}` on any menu change, and the list keys on open/close changes, is enough.

---

## 2. Background processing and the worker process

**Problem:** work the user doesn't need to wait for (sending notifications, building summaries) runs inside their request.

**Solution:** put a *job* somewhere durable and let another process run it.

```
API process (handles HTTP)            Worker process (handles jobs)
  PlaceOrder:                           loop:
    BEGIN                                 job = take next job (row lock)
      insert order                        run handler (e.g. send push)
      insert outbox(OrderPlaced)          success -> mark done
    COMMIT                                fail    -> retry later with backoff
    return 201                                       after N tries -> dead-letter
```

- **Same codebase, different entry point.** The worker loads the same modules and calls their job handlers. One build, one release, two start commands. This is *not* a microservice: no network API, no separate data.
- **Why a separate process?** **Isolation (a "bulkhead")**: a backlog of slow notifications can't take CPU/connections away from customers browsing. It can also be scaled separately.

---

## 3. Queues: PostgreSQL-backed vs message broker

**How a PostgreSQL queue works:** jobs are rows. Workers grab one with
```sql
SELECT * FROM jobs WHERE state = 'ready' AND run_at <= now()
ORDER BY run_at FOR UPDATE SKIP LOCKED LIMIT 1;
```
`SKIP LOCKED` lets many workers pull jobs concurrently without fighting over the same row. Libraries: **pg-boss** (Node), **Hangfire** (.NET, with PostgreSQL storage), **Oban** (Elixir), **good_job / Solid Queue** (Ruby), **River** (Go).

| | PostgreSQL queue (chosen for Q2) | RabbitMQ (earlier choice) | Kafka / cloud streams |
|---|---|---|---|
| New infrastructure | **None** | A broker cluster (or managed, e.g. CloudAMQP) | Large cluster or a managed service |
| Atomic with our data writes | **Yes, same transaction** | No (dual-write → outbox still needed) | No |
| Throughput | Hundreds-thousands of jobs/sec, far above our need | Tens of thousands/sec | Millions/sec |
| Routing / fan-out | Done in code (dispatcher) | Built-in exchanges | Topics + consumer groups |
| Ops burden for a team without DevOps | **Lowest** | Medium | High |
| When it's the right answer | Small-mid scale, one database | Many independent consumers, cross-service messaging | Event streaming, replay, analytics pipelines |

**The dual-write problem (why the outbox matters):**
```
save order in DB  ✔
-- process crashes here --
publish "OrderPlaced" to broker  ✘ never happens → restaurant never notified
```
Two systems can't be updated atomically without heavy machinery (two-phase commit). The **transactional outbox** fixes it: write the event *into the same database transaction* as the data, then publish from the table afterwards. With a PostgreSQL queue the job table can be in the same database, so the outbox and queue are almost the same thing.

---

## 4. Delivery guarantees and idempotency

| Guarantee | Meaning | Reality |
|-----------|---------|---------|
| At-most-once | Might be lost, never duplicated | Fire-and-forget |
| **At-least-once** | Never lost, **might be duplicated** | What outbox + retries give you |
| Exactly-once | Never lost, never duplicated | Not achievable end-to-end in practice. You *simulate* it with at-least-once + idempotent handlers |

**Idempotent** = doing it twice has the same effect as doing it once.
Techniques: a unique constraint on `(event_id, recipient)`, "check state before acting" (don't mark DELIVERED twice), or recording processed message IDs (an "inbox" table). **This becomes central in Q3.**

**Retries:** exponential backoff (10 s, 30 s, 2 min, 10 min...) plus jitter (randomness, so retries don't arrive in waves). After N attempts → **dead-letter** for a human to look at.

---

## 5. Read replicas and replication lag

- **Streaming replication:** the primary ships its write-ahead log (WAL) to the replica, which replays it, giving a read-only copy that is usually < 1 s behind.
- **Use for:** heavy reads that tolerate slightly old data (reports).
- **Don't use for:** "read your own write" screens. After a customer places an order, read the order from the primary or they may briefly not see it.
- **Pre-aggregation:** a nightly job computes `daily_summary` rows so dashboards don't scan raw orders. The next step up would be materialised views, then a warehouse (BigQuery/Snowflake/Redshift) if analytics grow.

---

## 6. Zero-downtime deployments

| Strategy | How | Notes |
|----------|-----|-------|
| **Rolling** (chosen) | Replace instances one by one, each must pass `/health` before receiving traffic | Built into most PaaS. Old and new versions run side by side briefly. |
| Blue-green | Run a full second environment, switch traffic at once | Fast rollback, double cost during the switch. |
| Canary | Send a small % of traffic to the new version first | Needs traffic-splitting tooling. Feature flags give us a cheaper version of this. |

**Expand → migrate → contract (parallel change)** for database changes:
1. *Expand*: add the new column/table; old code ignores it.
2. *Migrate*: deploy code that writes both / reads new; backfill.
3. *Contract*: once nothing uses the old column, drop it in a later release.
Never rename or drop in one step during a rolling deploy.

**Feature flags:** `if (flags.isOn('self-service-onboarding', restaurantId))`. Decouples *deploy* (code is live) from *release* (users see it). Start with a simple `feature_flags` table, and only buy a service (Unleash, LaunchDarkly, ...) if needed. **Remove flags after rollout**, or they become debt.

---

## 7. Enforcing module boundaries

| Layer of enforcement | Tool | Catches |
|----------------------|------|---------|
| Code imports | Architecture tests (dependency-cruiser / eslint-plugin-boundaries / ArchUnit / NetArchTest) | `ordering` importing `payments/features/...` |
| Database | One PostgreSQL role per module with `GRANT` only on its schema | SQL from Ordering into `payments.*` |
| People | `CODEOWNERS` + required reviews | Unreviewed changes to a module's public API |
| Contracts | Treat module APIs/events as versioned contracts (additive changes only) | Breaking another module silently |

---

## 8. Quick glossary

| Term | One line |
|------|----------|
| Cache-aside | App checks cache, falls back to DB, fills cache. |
| TTL | Time after which a cached value expires automatically. |
| Outbox | Table of events written in the same transaction as the data change. |
| Dual-write | Updating two systems without atomicity; one can succeed while the other fails. |
| SKIP LOCKED | PostgreSQL feature letting many workers pull different queue rows at once. |
| At-least-once | Messages never lost but possibly duplicated. |
| Idempotent | Safe to repeat. |
| Dead-letter | Where messages that keep failing go for human attention. |
| Bulkhead | Isolating resources so one overloaded part can't sink the rest. |
| Read replica | Read-only, slightly delayed copy of the database. |
| Rolling deploy | Replace instances gradually, behind health checks. |
| Expand/contract | Make DB changes in backward-compatible steps. |
