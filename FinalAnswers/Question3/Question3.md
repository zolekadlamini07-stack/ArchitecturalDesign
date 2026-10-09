# Question 3 - Friday Night: The Payment Provider Fails

> **Stage:** 50 restaurants, 3 cities (10 more planned in 2 years), much larger customer and driver base, **12 developers in 2 teams**.
>
> **One-line answer:** We stay a **modular monolith with vertical slices**. The fix is **inside the Payments and Ordering modules**, not a new topology. Payments now treats every provider call as **"may have happened, may not have"**. It records every attempt *before* calling the provider and uses **idempotency keys** at every hop. It runs provider calls **only in the worker** behind a **circuit breaker**, publishes results through the Q2 **outbox**, and **reconciles** anything uncertain through webhooks, a sweeper job and a daily settlement check. The order is **always created before any money is requested**, so "charged but no order" can't happen.
>
> Diagram: [`diagrams/Q3-Architecture.drawio`](diagrams/Q3-Architecture.drawio). Page 1 is the architecture (`[NEW Q3]` / `[CHANGED Q3]`), page 2 the order and payment state machines, page 3 the timeout → recovery sequence.
> Decisions and alternatives: [`../DECISION-LOG.md`](../DECISION-LOG.md) entries **D18-D25**. Concepts: [`Research.md`](Research.md).
> The acquisition challenge is answered separately in [`../Challenge/Challenge.md`](../Challenge/Challenge.md).

---

## 0. Where the system is before Friday (Q2 → Q3 growth changes)

"The system has evolved from the original version". These are the growth-driven changes we made *before* the incident. They're short because the brief's focus is the incident.

| Change | Problem it solves |
|--------|-------------------|
| **Two teams own modules.** *Team Ordering & Payments*: Identity, Customers, Ordering, Payments, Notifications. *Team Restaurants & Delivery*: Restaurants, Delivery, Reporting. | 12 people in one codebase need clear ownership. Module boundaries become *team* boundaries (Conway's law used on purpose). |
| **Module public APIs and events get contract tests** owned by the providing team. | Team A can't silently break Team B. |
| **City becomes a first-class concept**: `city_id` on restaurants, browse/cache/reporting per city, times stored in UTC, `Money` always carries a currency. | 3 cities now, 13 in two years. Expansion becomes *configuration* (add a city row), not code. |
| **Worker gets separate queues with their own concurrency limits**: `payments`, `notifications`, `integration`, `reporting`. | A backlog in one kind of job can't starve the others (a bulkhead). **This turns out to matter on Friday.** |
| **More API instances (auto-scaling on the PaaS), DB tier up, replica kept.** | Load. |
| **Still one deployable.** | Two teams can share a well-bounded monolith with CI + rolling deploys. Payments is now **extraction-ready** (it only talks via its API/events and owns its schema). See "Deliberately not done". |

---

## 1. What went wrong on Friday, traced to the Q2 design

The Q2 payment flow was still Q1's: `PlaceOrder` called `Payments.authorise()` **synchronously inside the HTTP request**, treated a **timeout as a failure**, and had **no idempotency** protection.

| Symptom from the brief | Why the Q2 design produced it |
|------------------------|-------------------------------|
| Payment requests take a long time | Each `PlaceOrder` request waits on the provider. Web threads and DB connections are held for seconds. |
| Requests time out / fail | Treated as "payment failed", even when the provider actually processed them. |
| **Payment succeeded but we never got the response** | Our code recorded **FAILED** for a payment that really **succeeded**. Nothing ever asked the provider again. |
| Customers don't know if they paid | The screen said "failed", but their banking app shows a hold. |
| Customers click Place Order multiple times | Each click = a new `PlaceOrder` = a **new order + new authorisation**. Duplicates. |
| Some restaurants get orders, some don't | Orders whose authorisation "failed" (by timeout) were never shown to restaurants, even when money was held. Slow requests crashing mid-way left orders half-processed. |
| **Charged but can't see the order** | The authorisation succeeded at the provider, our side marked the order failed / the request died, and the customer's order list hides failed orders. |
| Support flooded | No single place shows "what really happened to this order and its money". |
| Provider recovers, but our data is still wrong | Nothing reconciles our records with the provider's. |

**The root mistake:** we modelled a payment as **binary (success/fail)**. Over a network it is **ternary: succeeded, failed, or *unknown***. Everything below follows from taking "unknown" seriously.

---

## 2. The changes (all inside existing modules)

| # | Change | Module | Problem it solves |
|---|--------|--------|-------------------|
| C1 | **Create the order first**, then request payment via an event (`OrderPlaced` → outbox → Payments) | Ordering, Payments | "Charged but no order" becomes impossible: a payment request can only exist *for an existing order*. |
| C2 | **Payment attempt ledger** with explicit `UNKNOWN` state, written *before* calling the provider + an append-only `payment_events` audit log | Payments | Timeouts stop being lies. We always know what we *asked* and what we *heard*. |
| C3 | **Idempotency keys at three levels**: client → `PlaceOrder`, job/event handlers, our attempt id → provider | Ordering, Payments | Double clicks, redelivered jobs and retries can't create duplicate orders or charges. |
| C4 | **Provider calls only in the worker** (`payments` queue, max N concurrent) behind **timeouts + circuit breaker + retry with backoff and jitter** | Payments | A sick provider can't hang web requests or other job types. Failure is isolated. |
| C5 | **Reconciliation, three layers**: provider **webhooks** (with an inbox for de-duplication) + **sweeper job** for `UNKNOWN`/stuck attempts + **daily settlement** comparison | Payments | We converge to the truth after the fact, automatically. |
| C6 | **Compensation rules**: late success for a given-up order → **void**; captured but no valid order → **refund** | Ordering, Payments | No customer keeps a charge for food they didn't get. |
| C7 | **"Confirming your payment" UX** + **one pending order per customer** rule | Ordering + client | Customers stop guessing, and repeated clicks return the same order. |
| C8 | **Support timeline + alerts**: `GetOrderTimeline` slice (order history + payment events + notifications), dashboards/alerts on circuit state, `UNKNOWN` count, oldest pending order age, dead-letter count | Ordering (support slice), Payments, platform | "How do we know what happened after the fact?" |

---

## 3. Where state lives and what states exist

### Where payment state lives
**The `payments` schema, owned only by the Payments module.**

| Table | Purpose |
|-------|---------|
| `payment_attempts` | One row per attempt to authorise an order. `attempt_id` (**also the idempotency key sent to the provider**), `order_id`, `amount`, `currency`, `status`, `provider_payment_id`, timestamps. Unique: at most **one live attempt per order**. |
| `payment_events` | **Append-only** log of everything: request sent, response received, timeout, webhook received, reconciliation result, void/capture/refund. Never updated, only inserted. The audit trail. |
| `provider_webhook_inbox` | Raw webhooks, unique on the provider's event id, so the same webhook processed twice has no extra effect. |
| `outbox` (payments) | Events to publish (`PaymentAuthorised`, `PaymentDeclined`, ...), written in the same transaction as the status change. |

### Where order state lives
**The `ordering` schema, owned only by the Ordering module.** `orders.status` + `order_status_history` (every transition, who and why) + `place_order_requests` (idempotency keys from clients).

**Ordering never stores *how* payment is going**, only what it means for the order. Payments never decides *what happens to the order*. They agree through events.

### Payment attempt states (Payments module)

```
REQUESTED --(worker picks up, writes IN_FLIGHT, then calls provider)--> IN_FLIGHT
IN_FLIGHT --provider says yes------------> AUTHORISED --capture--> CAPTURED --refund--> REFUNDED
IN_FLIGHT --provider says no (decline)---> DECLINED            (final)
IN_FLIGHT --timeout / 5xx / no answer----> UNKNOWN   --reconcile--> AUTHORISED or DECLINED
AUTHORISED --order rejected / given up---> VOIDED              (final, customer never charged)
```
`UNKNOWN` is the key new state. It means *"we asked; we don't know the answer yet"*. It is **never** shown to anyone as "failed".

### Order states (Ordering module)

| State | Meaning | Next |
|-------|---------|------|
| `PAYMENT_PENDING` | Order saved, payment being confirmed (includes when Payments is `UNKNOWN`) | `AWAITING_ACCEPTANCE`, `PAYMENT_FAILED` |
| `PAYMENT_FAILED` | No money will be taken: definite decline, **or** we gave up after the confirmation window (any late hold is auto-released) | final (customer may start a new order) |
| `AWAITING_ACCEPTANCE` | Payment **authorised**. *Only now* does the restaurant see the order | `ACCEPTED`, `REJECTED` |
| `REJECTED` | Restaurant declined. The payment hold is voided | final |
| `ACCEPTED` | Restaurant accepted. Capture is requested in the background | `PREPARING` |
| `PREPARING` → `READY_FOR_PICKUP` → `OUT_FOR_DELIVERY` → `DELIVERED` | As before | |
| `CANCELLED` | Cancelled by support / customer before preparation (void or refund issued) | final |

**What changed from Q1/Q2:** `PLACED` became `PAYMENT_PENDING` (an explicit, visible state customers can see, not a split-second step), and the restaurant only sees orders from `AWAITING_ACCEPTANCE` onwards.

---

## 4. The new happy path (so the failure cases make sense)

```
Customer          API (Ordering)                 Worker (Payments)                 Provider
   | POST /orders     |                                 |                                |
   | Idempotency-Key K|                                 |                                |
   |----------------->| TX: insert place_order_request(K)                                |
   |                  |     insert order (PAYMENT_PENDING)                               |
   |                  |     insert outbox OrderPlaced   |                                |
   |<-- 202 {orderId} | COMMIT                          |                                |
   | "Confirming your |                                 |                                |
   |  payment..."     |   OrderPlaced job ------------->| TX: insert attempt A (REQUESTED)|
   | (polls status)   |                                 | set IN_FLIGHT, log request      |
   |                  |                                 |-- authorise, key=A, 10 s ----->|
   |                  |                                 |<------- authorised ------------|
   |                  |                                 | TX: A=AUTHORISED, log response, |
   |                  |                                 |     outbox PaymentAuthorised    |
   |                  |<-- PaymentAuthorised job -------|                                 |
   |                  | TX: order -> AWAITING_ACCEPTANCE, outbox OrderAwaitingAcceptance |
   |  status update   |   -> notify restaurant (notifications queue)                     |
```
Normally this whole path takes **1-2 seconds**. The customer sees "Confirming your payment..." briefly, then "Waiting for the restaurant".

---

## 5. Walkthrough: exactly what happens when...

### 5.1 ...the payment request times out
1. The worker has already written attempt `A` as **`IN_FLIGHT`** and logged "request sent" **before** calling the provider.
2. No answer within **10 s** → the HTTP client gives up.
3. We **do not** write `DECLINED`. We write **`UNKNOWN`** + a `payment_events` row "timeout after 10 s".
4. The circuit breaker counts the timeout. The job is **not** marked failed. A **reconciliation check** for `A` is scheduled (in 30 s, then backing off).
5. **Ordering is told nothing new.** The order stays `PAYMENT_PENDING`, and the customer still sees *"We're confirming your payment. Please don't place the order again; we'll update you here."*
6. The restaurant sees nothing yet, which is correct, because we don't know if we have the money.

### 5.2 ...the payment actually succeeded despite the timeout
The provider *did* authorise `A`. We just didn't hear. We find out through **whichever of three paths arrives first** (all three are safe to arrive in any order, any number of times):

| Path | How it finds out |
|------|------------------|
| **Webhook** | The provider sends `payment_intent.amount_capturable_updated` for `A`. Our `ReceiveProviderWebhook` slice verifies the signature, stores it in the inbox (de-duplicated) and replies 200. The worker applies it: `UNKNOWN → AUTHORISED`. |
| **Sweeper (reconciliation job)** | Every minute: for each `UNKNOWN` or stale `IN_FLIGHT` attempt, **repeat the same request with the same idempotency key `A`**. The provider recognises the key and **returns the original result instead of charging again**. If the key has expired at the provider (e.g. after 24 h), look up the payment by our reference `A` stored in its metadata. |
| **Daily settlement check** | Next morning, compare the provider's report of all authorisations/captures with our ledger. Anything still mismatched goes to a support exception queue. This is the net under the net. |

Once `A = AUTHORISED` (in one transaction with the `PaymentAuthorised` outbox row), the normal path continues:
- **If the order is still `PAYMENT_PENDING`** (we're within the **10-minute confirmation window**) → order `AWAITING_ACCEPTANCE`, restaurant notified, customer sees "Waiting for the restaurant". *Late, but correct.*
- **If we had already given up** (order `PAYMENT_FAILED` after the window) → Ordering replies to `PaymentAuthorised` with **"not needed"**, and Payments **voids** the authorisation. The customer is told *"The hold on your card has been released; you have not been charged."* We never surprise a restaurant with a 40-minute-old order.

### 5.3 ...the customer retries (clicks Place Order again)
Three cases, three protections:

| What the customer does | What protects them |
|------------------------|--------------------|
| **Double-clicks / the app retries the same request** | The client generates **one idempotency key `K` per checkout** and sends it in the `Idempotency-Key` header. `place_order_requests` has a **unique constraint on (customer_id, K)**. The second request finds `K` and **returns the same order** (same 202 response). No second order, so no second payment. |
| **Refreshes the page and checks out again** (new key `K2`) | **One pending order per customer**: a partial unique index on `orders(customer_id) WHERE status = 'PAYMENT_PENDING'`. `PlaceOrder` returns *"You already have an order being confirmed"* and shows it. Enforced by the database, so it holds even with two API instances racing. |
| **Order ended `PAYMENT_FAILED` (definite decline) and they try another card** | A **new attempt** with a new attempt id for the **same order**, which is allowed because the previous attempt is final (`DECLINED`). Never reuse an idempotency key with different details. |

### 5.4 ...the same payment request is received twice
"The same request" can come twice from **our own machinery**: the job queue delivers at-least-once, a worker can crash after doing the work but before marking the job done, and the provider can send a webhook twice.

| Duplicate | Why it's harmless |
|-----------|-------------------|
| `OrderPlaced` job runs twice | Payments inserts the attempt with a **unique constraint on `order_id` for live attempts**. The second run finds attempt `A` and continues from its current status instead of creating `A2`. |
| The authorise call for `A` is sent twice | Same **idempotency key `A`** → the provider returns the first result and never charges twice. |
| `PaymentAuthorised` event processed twice | Ordering's handler checks state first: the order is already `AWAITING_ACCEPTANCE`, so it does nothing. **State transitions are idempotent** (the state machine rejects/ignores a repeat). |
| Webhook delivered twice | `provider_webhook_inbox` unique on the provider's event id → the second one is dropped. |
| Restaurant notification job runs twice | `notification_log` unique on `(event_id, recipient)` (from Q2). |

### 5.5 ...there are crashes after payment succeeds
Walk through every point the process could die:

| Crash point | What survives | How we recover |
|-------------|---------------|----------------|
| After the provider authorised, **before** we saved the result | Attempt `A` is `IN_FLIGHT` in our DB. The job's lease expires. | The job runs again → same key `A` → the provider returns the stored "authorised" → we save it. The sweeper would also catch the stale `IN_FLIGHT`. |
| **After** saving `AUTHORISED`, before publishing the event | The `PaymentAuthorised` outbox row was committed **in the same transaction** as the status. | The outbox dispatcher publishes it on restart. **It can't be lost.** |
| After the event, before Ordering updated the order | The job is still in the queue (not acknowledged). | Retried, and the handler is idempotent. |
| After the order moved to `AWAITING_ACCEPTANCE`, before the restaurant was notified | The notification job is in the queue. | Retried. **And** the restaurant dashboard polls `ListIncomingOrders`, so the order appears regardless. A push is a nudge, not the source of truth. |
| After `ACCEPTED`, before capture succeeded | The `OrderAccepted` → capture job is in the `payments` queue. | Capture is retried with key `capture-A` until it succeeds. **The restaurant keeps cooking**: an authorisation holds the money for ~7 days. Repeated capture failure → alert + support queue. |

**What happens if payment succeeds but order creation fails?** With C1 that ordering **can't occur**. The order is committed *before* Payments ever sees an `OrderPlaced` event, so there is never a payment without an order. The remaining real-world variant ("money held but the order can't go ahead", e.g. given up or rejected) is handled by **compensation**: void the hold (or refund if captured), log it in `payment_events` and tell the customer.

### 5.6 ...the payment provider comes back online
1. **Circuit breaker.** During the outage it went **OPEN** (e.g. >50% failures/timeouts over 20 calls in 30 s). While open, the worker **doesn't call the provider at all**. Payment jobs are rescheduled, so we stop piling load on a sick provider and stop wasting 10 s per attempt. After a cool-down (e.g. 30 s) it goes **HALF-OPEN** and lets **a few trial calls** through. If they succeed it **CLOSES**, and normal flow resumes with no human involved.
2. **Controlled drain.** The backlog of `REQUESTED` attempts runs through the `payments` queue at its **concurrency limit** (e.g. 10 at a time) with **jitter**, so we don't hit the recovering provider with thousands of calls at once (a "thundering herd").
3. **Reconciliation catches up.** The sweeper resolves every `UNKNOWN` attempt (same-key retry/lookup), and the provider's **queued webhooks** arrive (providers retry webhooks for hours/days) and are de-duplicated by the inbox.
4. **Each pending order resolves** to `AWAITING_ACCEPTANCE` (within its window) or to `PAYMENT_FAILED` + void (outside it). Customers get a notification either way.
5. **Next morning** the daily settlement check confirms the ledger matches the provider exactly. Any leftover goes to support.

### 5.7 ...failed operations need to be retried
**What is retried, and how:**

| Failure type | Retry? | How |
|-------------|--------|-----|
| Timeout / connection error / provider 5xx / 429 rate-limit | **Yes, automatically** | Same idempotency key, exponential backoff with jitter (30 s, 1 m, 2 m, 5 m, ...), only while the circuit is closed/half-open. |
| Outcome `UNKNOWN` | **Not retried blindly; *resolved*** | Reconciliation (same-key replay / lookup / webhook). |
| **Definite decline** (insufficient funds, card blocked, fraud) | **Never automatically** | That's a real answer. The order goes `PAYMENT_FAILED` and the customer may try another card. |
| Capture / void / refund | **Yes, automatically** | Their own keys (`capture-A`, `void-A`, `refund-A-1`). |
| Our own job handler bug | Retried N times → **dead-letter** | Alert. A human fixes the bug, then re-queues from the admin screen. |

**Rule:** *only retry what is idempotent, and only with the same key.*

### 5.8 How does the architecture recover? (summary)
| Layer | Mechanism | Recovers from |
|-------|-----------|---------------|
| **Prevent** | Order-first, idempotency keys (client, handlers, provider), one-pending-order rule | Duplicates, "charged but no order" |
| **Contain** | Provider calls only in worker, own queue/concurrency (bulkhead), timeouts, circuit breaker | Slow provider dragging down the platform |
| **Survive crashes** | Ledger written before calling, outbox in the same transaction, at-least-once jobs + idempotent handlers | Lost results, lost events |
| **Converge** | Webhooks + sweeper + daily settlement | `UNKNOWN`s, missed responses |
| **Compensate** | Void/refund rules | Money held for orders that won't happen |
| **Explain** | `payment_events`, `order_status_history`, `GetOrderTimeline` support view, correlation id = order id in all logs, alerts | Support questions, post-incident review |

---

## 6. "Questions to consider", answered directly

| Question | Answer |
|----------|--------|
| **Where does payment state live?** | `payments` schema, owned by the Payments module: `payment_attempts` + append-only `payment_events` + webhook inbox. |
| **Where does order state live?** | `ordering` schema, owned by Ordering: `orders.status` + `order_status_history`. |
| **What states can an order have?** | `PAYMENT_PENDING`, `PAYMENT_FAILED`, `AWAITING_ACCEPTANCE`, `REJECTED`, `ACCEPTED`, `PREPARING`, `READY_FOR_PICKUP`, `OUT_FOR_DELIVERY`, `DELIVERED`, `CANCELLED` (section 3). |
| **Payment succeeds but order creation fails?** | Can't happen: the order is created first, in its own committed transaction, and payment is requested from it. The real-world variant (money held, order can't proceed) → automatic void/refund (5.5). |
| **How do we prevent duplicate payments?** | One attempt per order (DB unique), idempotency key per attempt sent to the provider, client idempotency key on `PlaceOrder`, one pending order per customer. |
| **How do we safely retry?** | Only idempotent operations, always with the same key, exponential backoff + jitter, behind a circuit breaker, bounded attempts → dead-letter. Never auto-retry definite declines. |
| **Which operations should be synchronous?** | Things the user is waiting on that touch **only our own database**: browse, basket quote, **recording** the order (`PlaceOrder` returns 202 once saved), restaurant accept/reject, status updates, history. |
| **Which should be asynchronous?** | **Everything that calls a third party or can be slow**: authorise/capture/void/refund, notifications, reconciliation, reporting summaries, integration calls. |
| **Where should failures be isolated?** | At the **Payments module's adapter**, in the worker's **`payments` queue** (own concurrency = bulkhead) behind a **circuit breaker**. A provider outage delays payment confirmation. Browsing, restaurant dashboards, deliveries in progress and notifications for other events are unaffected. |
| **How do we know what happened after the fact?** | `payment_events` (every request/response/webhook), `order_status_history`, `notification_log`, structured logs with the **order id as correlation id**, and the `GetOrderTimeline` support view. |
| **How do we reconcile with the provider?** | Real-time: webhooks. Near-real-time: sweeper (same-key replay / lookup by reference). Daily: settlement report matched against the ledger, with mismatches going to a support exception queue. |

---

## 7. What we deliberately did **not** do

| Not done | Why | Revisit when |
|----------|-----|--------------|
| **Extract Payments into a microservice** | The incident is about an **external** dependency's ambiguity. Extraction doesn't remove any of it, and it *adds* a network hop between Ordering and Payments (one more place for "unknown" outcomes). Isolation is achieved with the worker queue + circuit breaker. | Payments needs its own release cadence/team, separate PCI scope, or very different scaling. It is extraction-ready (own schema, events only). |
| **A second payment provider with automatic failover** | Doubles integration, reconciliation and compliance work. Needs card re-tokenisation across providers, and the ambiguity problem appears in both. | Provider outages become frequent/costly enough to justify it. |
| **Distributed transactions (two-phase commit) / sagas framework** | The provider can't join our transactions anyway. Outbox + idempotency + compensation **is** a simple, explicit saga. | Many multi-step, multi-service workflows appear. |
| **Event sourcing** | The append-only `payment_events` log gives us the audit trail without rebuilding state from events everywhere. | Audit/replay needs grow beyond payments. |
| **Message broker (Kafka/RabbitMQ)** | The PostgreSQL queue still handles our volume, and a broker doesn't fix ambiguity. | Throughput or independent consumers outgrow PostgreSQL (watch queue latency). |
| **Multi-region infrastructure** | 3 cities ≠ 3 regions. One region serves them fine. | Latency or data-residency requirements in new markets. |

## Q2 → Q3 at a glance

| Aspect | Q2 | Q3 |
|--------|----|----|
| Topology | Modular monolith + worker | **Same** (+ separate worker queues with own concurrency) |
| Payment call | Synchronous in `PlaceOrder` | **Asynchronous in worker**, triggered by `OrderPlaced` |
| Payment outcomes | success / fail | **authorised / declined / UNKNOWN**, resolved by reconciliation |
| Duplicate protection | None | **Idempotency keys** (client, handlers, provider) + DB uniqueness |
| Provider failure handling | Timeout = failure | **Timeouts + circuit breaker + backoff + bulkhead** |
| After-the-fact truth | Provider dashboard by hand | **Webhooks + sweeper + daily settlement**, `payment_events` audit, support timeline |
| Restaurant sees order | After a "successful" synchronous auth | **Only after `PaymentAuthorised`** (via outbox) |
| Teams | 5 devs, CODEOWNERS | **2 teams owning modules, contract tests** |
| Cities | 1 | **City as data (`city_id`)**, ready for 10 more |
