# Final Integration Challenge - Integrating an Acquired Food-Delivery Company

> **The brief:** We acquired a smaller company with its own Customer, Restaurant, Order and Payment systems. Integrate **without rewriting either platform**, while staying consistent with our architectural approach.
>
> **One-line answer:** Add **one new module to our modular monolith: `PartnerIntegration`**. It is an **Anti-Corruption Layer (ACL)**: the *only* code that knows the acquired platform exists. It talks to their APIs through **adapters**, translates their model ↔ ours, owns an **ID-mapping** table, and speaks to the rest of our system **only through our existing module public APIs and events**. Each platform stays the **owner of its own data**. Failures on their side are contained with the same tools as Q3 (worker queue, circuit breaker, idempotency, reconciliation).
>
> Diagram: [`diagrams/Challenge-Integration.drawio`](diagrams/Challenge-Integration.drawio). Decisions: [`../DECISION-LOG.md`](../DECISION-LOG.md) **D26-D29**. Concepts: section 9 below + `../../Question3/Research/04-AntiCorruptionLayerIntegration.md`.

---

## 1. First: decide what "integrate" means (scope)

"Integrate" can mean ten different projects. Picking the smallest one that delivers value is the architectural decision. Phase 1:

| Goal | In scope? | Why |
|------|-----------|-----|
| **Our customers can browse and order from the acquired company's restaurants** | **Yes** | The commercial reason for the acquisition: more restaurants on our platform, immediately. |
| **The business sees combined orders, revenue and deliveries** | **Yes** | The brief's business need ("see orders and revenue") now spans both companies. |
| Their customers keep using their app, unchanged | **Yes (by not touching it)** | "Without rewriting either platform." |
| Merge customer accounts across platforms | **No (Phase 2, optional)** | Needs consent, de-duplication and identity linking. Not required to get value. |
| Move their restaurants onto our platform | **No (maybe later, via strangler fig)** | That is a migration, which the brief excludes. The ACL makes it possible later, one restaurant at a time. |
| Merge payment systems | **No** | Each platform takes payment for orders placed **on that platform**. Money between the companies is settled by finance, not by integrating payment systems. |

---

## 2. Data ownership: who is the source of truth?

| Data | Owner (source of truth) | What the other side holds |
|------|-------------------------|---------------------------|
| Their restaurants & menus | **Their Restaurant system** | We hold a **read-only copy** in our `restaurants` schema, marked `source = PARTNER`, refreshed by sync. Our UI can't edit it. |
| Our restaurants & menus | **Us** | Nothing (we don't publish to them in Phase 1). |
| An order placed **on our platform** for one of their restaurants | **Our Ordering module**, for the customer-facing order (status, history, tracking) | **Their Order system** owns the *fulfilment* (kitchen, their drivers). It receives a forwarded copy with **our order id as its external reference**. |
| Payment for that order | **Our Payments module** (our customer, our provider, same Q3 flow) | Nothing. Their Payment system is not involved. |
| Their customers, orders and payments placed on **their** platform | **Them** | Only **aggregated** daily figures for reporting. |
| ID mapping (our id ↔ their id) | **`PartnerIntegration` module** (schema `partner_integration`) | - |

**Rule:** never dual-write and never share a database. Each fact has exactly one owner, and copies are labelled as copies.

---

## 3. The integration module (ACL) and its boundaries

```
 OUR PLATFORM (modular monolith)                                       ACQUIRED PLATFORM (unchanged)

 Restaurants  Ordering  Payments  Reporting  ...                       Restaurant API   Order API
      ^          ^  |                ^                                  Webhooks ------------+
      |  public  |  | events         |                                       ^               |
      |  APIs    |  v                |                                       |               v
 +----------------------------------------------------------------+         |               |
 |  PartnerIntegration module (Anti-Corruption Layer)             |         |               |
 |   features/  sync-partner-catalogue/   forward-order/          |---------+               |
 |              receive-partner-webhook/  pull-partner-reports/   |<------------------------+
 |   translation/  (their model <-> our model, status mapping)    |
 |   adapter/      PartnerApiClient  (the ONLY code that knows    |
 |                 their URLs, auth, field names, versions)       |
 |   data/         id_map, partner_inbox, outbox                  |
 +----------------------------------------------------------------+
```

Why this is **consistent with our architectural approach**:
- **It's a module**, with its own schema, public API/events and slices. It's the same shape as every other module.
- **Vertical slices**: each integration flow is one feature folder.
- **Ports & adapters at the edge**: their API sits behind `PartnerApiClient`, like the payment provider sits behind `PaymentProvider`.
- **The rest of our system never learns their model.** Ordering, Restaurants and Reporting see *our* concepts only. If we deleted this module, nothing else would fail to compile.
- **Runs in the worker** for anything that calls them, on its own `integration` queue (a bulkhead), so their outages can't affect our payments or notifications.

**Why a module and not a separate service?** Our topology is a modular monolith, and nothing about the integration needs separate scaling or deployment yet. A separate service would add a network hop and a deployment pipeline without solving a problem. **Revisit** if the partner's traffic volume or release schedule diverges from ours. Being a well-bounded module, it can be extracted cleanly.

---

## 4. The flows

### 4.1 Their restaurants appear on our platform (catalogue sync)
1. `SyncPartnerCatalogue` (worker, every few minutes **and** on their "menu changed" webhook) calls their Restaurant API.
2. **Translate**: their `Store{store_id, menu{sections[{products[{sku, price_cents, in_stock}]}]}}` → our `Restaurant` + `MenuItem` (name, `Money`, `is_available`, `city_id` from their address).
3. Call **our** `Restaurants.upsertPartnerRestaurant(...)` public API, keyed by our id from `id_map` (creating a mapping on first sight). Restaurants module stores it with `source = PARTNER` and invalidates its cache (Q2 logic, unchanged).
4. Customers browse it exactly like any other restaurant. **Our browse path never calls their API live**, so their outage never slows our browsing.

### 4.2 A customer orders from one of their restaurants
1. **Our** `PlaceOrder` runs unchanged: order `PAYMENT_PENDING` → our Payments authorises (Q3 flow) → `PaymentAuthorised` → order `AWAITING_ACCEPTANCE`.
2. Ordering publishes `OrderAwaitingAcceptance` as usual. The order carries `fulfilment = PARTNER`, a **generic** field. Ordering doesn't know *who* the partner is.
3. `PartnerIntegration.ForwardOrder` subscribes for partner restaurants: it translates our order → their order format and calls their Order API with **idempotency key = our order id** (or, if their API has no idempotency support, first checks "does an order with external_ref = our id exist?").
4. Their restaurant accepts/rejects **in their own tools**. Their system sends a webhook (or we poll) → `ReceivePartnerWebhook` stores it in `partner_inbox` (de-duplicated) → translates their status → calls **our** `Ordering.acceptOrder / rejectOrder / markPreparing / ... ` public API.
5. From there **our existing flow continues**: accept → capture, reject → void, delivered → complete. **Delivery is done by their drivers**, so our Delivery module is not involved for `fulfilment = PARTNER` orders, and their status updates move our order through `OUT_FOR_DELIVERY → DELIVERED`.

### 4.3 Combined reporting
`PullPartnerReports` (nightly) fetches their daily totals per restaurant/city → translates → writes `reporting.partner_daily_summary` (through Reporting's API). Reporting shows "Our platform / Acquired platform / Total". Per-order detail stays in their system, which is still the owner.

---

## 5. Model translation (where mismatches are absorbed)

| Concept | Theirs (example) | Ours | Translation rule |
|---------|------------------|------|------------------|
| IDs | `store_id: 8812`, `order_no: "B-4410"` | UUIDs | `id_map(entity_type, our_id, their_id)`. Never expose their ids outside the module. |
| Money | `price_cents: 4500` | `Money(45.00, 'ZAR')` | Convert units and attach currency. |
| Order status | `NEW, CONFIRMED, COOKING, DISPATCHED, DONE, CANCELLED` | our states | Explicit mapping table: `CONFIRMED → ACCEPTED`, `COOKING → PREPARING`, `DISPATCHED → OUT_FOR_DELIVERY`, `DONE → DELIVERED`, `CANCELLED (before confirm) → REJECTED`, `CANCELLED (after) → CANCELLED + refund`. **An unknown status is an alert, not a guess.** |
| Availability | `in_stock: 0/1`, sometimes missing | `is_available: boolean` | Missing → `false` (safe default: don't sell what might not exist). |
| Menu shape | sections → products → modifiers | items (+ options) | Flatten/translate. Unsupported constructs (e.g. combos) are skipped and logged for product review. |

The translation lives in **`translation/`, one place, with tests built from real samples of their payloads**.

---

## 6. External system failures (their platform is just another external dependency)

| Failure | What happens |
|---------|--------------|
| Their API is slow/down during **catalogue sync** | Circuit breaker opens. We keep serving the **last synced copy**. If a sync hasn't succeeded for > 30 min, their restaurants are shown "temporarily unavailable" (so we don't take orders we can't forward). |
| Their API is down when **forwarding an order** | The `ForwardOrder` job retries with backoff (same idempotency key = our order id, so no duplicate orders on their side). The order sits in `AWAITING_ACCEPTANCE`, and the customer sees "waiting for the restaurant", as with any slow restaurant. **If not confirmed within 10 minutes → we auto-reject → our existing reject path voids the payment hold.** No new failure logic: we reuse Q3's. |
| A webhook from them is missed | A reconciliation job polls their Order API for our in-flight partner orders every few minutes (the same "never trust one channel" rule as payments). |
| They send a webhook twice / out of order | `partner_inbox` de-duplicates by their event id. Our order state machine **ignores transitions that go backwards** (e.g. `COOKING` arriving after `DISPATCHED`). |
| Their payload changes shape | The translation fails loudly → the message is dead-lettered with an alert. Our order data is never corrupted by a half-understood payload. |

---

## 7. Versioning and integration contracts

- **Pin their API version** in the adapter (e.g. `/v2/` or a version header). Upgrading is a deliberate, tested change in one module.
- **Contract tests** (consumer-driven, e.g. **Pact**): we publish *what we expect from their API*. Their CI runs our expectations, so a breaking change on their side fails **their** build before it reaches production. If they can't adopt Pact, we run nightly contract checks against their sandbox.
- **Our side is versioned too**: if they ever need to call us (Phase 2), they get a **separate, versioned partner API** (`/partner/v1/...`) from the `PartnerIntegration` module. They are never given our internal module APIs, events or database.
- **Tolerant reader**: our adapter ignores unknown fields, so *additive* changes on their side don't break us.

## 8. Coupling: how loosely are we tied?

| Coupling type | Level | How we keep it low |
|---------------|-------|--------------------|
| Code | **None outside one module** | Only `PartnerIntegration` imports partner types. An architecture test enforces this. |
| Data | **None shared** | No shared DB, no dual writes. Copies are labelled. The mapping is owned by one module. |
| Runtime / availability | **Low** | Async via worker queue + circuit breaker + last-known-good catalogue. Their outage = their restaurants temporarily unavailable, nothing else. |
| Semantic (model) | **Absorbed in translation** | Explicit mapping tables. Unknown values → alert. |
| Temporal (release schedules) | **Low** | Version pinning + contract tests. |

---

## 9. Concepts used (study notes)

| Concept | What it is | Where it appears |
|---------|-----------|------------------|
| **Anti-Corruption Layer** (Eric Evans, DDD) | A translating layer that stops another system's model from "corrupting" yours. It has facades, adapters and translators. | The whole `PartnerIntegration` module. |
| **Bounded context / context mapping** | Each system has its own model. The relationship between them is chosen explicitly (ACL, conformist, shared kernel, ...). | We choose **ACL** (we don't conform to their model, and they don't to ours). |
| **Adapter** (ports & adapters) | Code that implements our interface using their technology. | `PartnerApiClient`. |
| **Tolerant reader** (Martin Fowler) | Read only the fields you need, ignore the rest. | The adapter's parsing. |
| **Consumer-driven contracts** | The consumer writes tests of what it needs, and the provider runs them. | Pact against their API. |
| **Strangler fig** (Martin Fowler) | Replace a system gradually by routing pieces to the new one behind a stable facade. | The future option to migrate their restaurants one at a time behind the same ACL. |
| **Idempotency, outbox, inbox, circuit breaker, reconciliation** | From Q2/Q3. | Reused unchanged for the partner. |

## 10. Options considered (summary; full pros/cons in DECISION-LOG D26)

| Option | Verdict | Reason |
|--------|---------|--------|
| **ACL module inside our monolith** | **Chosen** | Consistent with our architecture, isolates their model, no new deployable. |
| Separate integration microservice | Not now | Same logic plus a network hop and a pipeline, with no scaling/deploy need yet. |
| Shared database / direct DB access to theirs | Rejected | Couples both schemas forever. Breaks data ownership on both sides. |
| Point-to-point: each of our modules calls their APIs directly | Rejected | Their model leaks into Ordering, Restaurants and Reporting. Every change on their side touches many of our modules. |
| Rewrite/migrate their platform onto ours | Rejected (by the brief) | Big-bang risk. The ACL keeps a gradual strangler-fig migration available if ever wanted. |
| Integration platform / ESB / iPaaS (MuleSoft, etc.) | Rejected | Heavy and costly. Business rules would end up in a tool outside our codebase and outside our tests. |

## 11. Deliberately not done
- **No customer account merging** (Phase 2, with consent).
- **No changes to their payment system**: each platform charges for its own orders.
- **No live calls to their API on our browse path**: we serve a synced copy.
- **No exposing our internals to them**: if they need us, they get a versioned partner API.
