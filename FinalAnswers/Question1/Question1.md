# Question 1 - Design the Architecture

> **Stage:** 1 restaurant, a few drivers, small customer base, 3 developers, very limited budget, no DevOps, prove the business model.
>
> **One-line answer:** A **modular monolith**: one deployable application and one PostgreSQL database. It is split into 8 business modules that each own their own data. Inside every module the code is organised as **vertical slices**, with one folder per use case.
>
> Diagram: [`diagrams/Q1-Architecture.drawio`](diagrams/Q1-Architecture.drawio). Page 1 is the system and page 2 is the inside of a module.
> Every technology/pattern choice below is explained in [`../DECISION-LOG.md`](../DECISION-LOG.md) (entries **D1-D10**).
> Background concepts: [`Research.md`](Research.md).

---

## How to read this answer

The question asks for 6 things the architecture must show and 3 things to identify. Each one has its own heading below, in the order the question lists them.

---

# Part 1 - What the architecture shows

## 1. The major components of the system

The system is **one application** (the modular monolith). It has **8 modules** and talks to a few **external systems**.

| # | Component | Kind | In one sentence |
|---|-----------|------|-----------------|
| 0 | **Web client** (customer / restaurant / driver / admin views) | Front-end | One responsive web app (installable as a PWA) with a different view per role. |
| 1 | **Identity** | Module | Who you are and what role you have (customer, restaurant staff, driver, admin). |
| 2 | **Customers** | Module | Customer profile and delivery addresses. |
| 3 | **Restaurants** (catalogue) | Module | Restaurant profile, opening status, delivery fee, **and the menu**. |
| 4 | **Ordering** | Module | The order and its lifecycle: place, accept/reject, status, history. **The heart of the system.** |
| 5 | **Payments** | Module | Talking to the payment provider: authorise, capture, void, refund. |
| 6 | **Delivery** | Module | Drivers, their availability, delivery requests and delivery status. |
| 7 | **Notifications** | Module | Sends push/email messages when something happens. |
| 8 | **Reporting** | Module | Orders, revenue and completed deliveries for the business. |
| - | **PostgreSQL** | Data store | One managed database with **one schema per module**. |
| - | **Payment provider** (e.g. Stripe) | External | Takes card details and moves money. |
| - | **Push/email provider** (e.g. Firebase Cloud Messaging + an email API) | External | Delivers notifications to devices. |
| - | **Hosting platform** (managed PaaS) | External | Runs the app and the database for us, because we have no DevOps. |

> **Changed from earlier material:** *Menu* is no longer a separate module. It is part of **Restaurants**. The menu has the same owner (the restaurant), changes for the same reasons and is read on the same screen ("browse a restaurant, see its menu"). Two modules would have meant a cross-module call on the busiest read path for no benefit. See `CHANGES-AND-REASONS.md` C3.

## 2. The responsibilities of each component

Each module's responsibilities are listed as its **use cases (slices)**. With vertical slice architecture, *a responsibility = a slice = a folder*. The brief's required capabilities map directly onto slices:

### Identity
| Slice | Who uses it | Brief requirement |
|-------|-------------|-------------------|
| `RegisterCustomer` | Customer | "Create an account" |
| `Login` / `Logout` | Everyone | - |
| `CreateRestaurantStaffAccount` | Admin (platform onboards the restaurant) | - |
| `CreateDriverAccount` | Restaurant staff (restaurant onboards its drivers) | - |

Identity issues a token that includes the user's **role** and, for staff and drivers, their **restaurantId**. Every other module trusts that token. They never look at passwords.

### Customers
| Slice | Brief requirement |
|-------|-------------------|
| `GetMyProfile` / `UpdateMyProfile` | - |
| `AddAddress` / `ListAddresses` | Needed to place an order |

### Restaurants (catalogue + menu)
| Slice | Brief requirement |
|-------|-------------------|
| `BrowseRestaurants` | Customer: "browse restaurants" |
| `GetMenu` | Customer: "view menus" |
| `AddMenuItem` / `UpdateMenuItem` / `RemoveMenuItem` / `SetItemAvailability` | Restaurant: "manage their menu" |
| `SetOpenStatus` / `UpdateRestaurantSettings` (delivery fee) | Restaurant |

**Public API used by other modules:** `getPricedItems(restaurantId, itemIds)` returns current names, prices and availability. Ordering uses it so that **the server, not the client, decides prices**.

### Ordering
| Slice | Brief requirement |
|-------|-------------------|
| `QuoteBasket` | Customer: "add items to a basket". The basket lives on the client. This slice recalculates totals on the server. |
| `PlaceOrder` | Customer: "place an order" + "pay for the order" (it asks Payments to authorise) |
| `TrackOrder` | Customer: "track the order" |
| `ListMyOrders` | Customer: "view previous orders" |
| `ListIncomingOrders` | Restaurant: "receive orders" |
| `AcceptOrder` / `RejectOrder` | Restaurant: "accept or reject orders" |
| `MarkPreparing` / `MarkReadyForPickup` | Restaurant: "update the order status" |
| `ListRestaurantOrderHistory` | Restaurant: "see previous orders" |

Ordering owns the **Order** and its **state machine** (the rules for which status can follow which). The state machine lives in one shared `Order` class inside the module, so every slice obeys the same rules (see D2 for why this is "pragmatic" VSA).

**Order states in Q1:**
```
PAYMENT_FAILED <- PLACED -> AWAITING_ACCEPTANCE -> ACCEPTED -> PREPARING -> READY_FOR_PICKUP -> OUT_FOR_DELIVERY -> DELIVERED
                                    |
                                    v
                                REJECTED   (payment hold is released)
```

### Payments
| Slice / public API | What it does |
|--------------------|--------------|
| `authorise(orderId, amount, paymentToken)` | Puts a **hold** on the customer's card for the order total. |
| `capture(orderId)` | Takes the held money. Called when the restaurant **accepts**. |
| `void(orderId)` | Releases the hold. Called when the restaurant **rejects**. |
| `refund(orderId)` | Returns money after a capture. This is a rare, manual/admin action in Q1. |

Payments is the **only** code that knows a payment provider exists. Everyone else calls `authorise/capture/void`.

### Delivery
| Slice | Brief requirement |
|-------|-------------------|
| `StartShift` / `EndShift` | Driver: "become available for deliveries" |
| `ListDeliveryRequests` | Driver: "receive delivery requests" |
| `AcceptDelivery` | Driver: "accept a delivery". The first available driver to accept wins. |
| `MarkPickedUp` / `MarkDelivered` | Driver: "update delivery status" |
| `ListMyDeliveries` | Driver: "see their current and previous deliveries" |

When an order becomes `READY_FOR_PICKUP`, Ordering calls `Delivery.requestDelivery(orderId, restaurantId, address)`. Drivers **belong to a restaurant** (decision kept from earlier work), so a request is shown only to that restaurant's on-shift drivers.

### Notifications
| Slice | What it does |
|-------|--------------|
| `Notify(userId, template, data)` | Sends a push notification (and email for receipts) through the provider. |

In Q1 this is a **plain in-process call** made after the main work has been saved (see risk R3, which Q2 fixes).

### Reporting
| Slice | Brief requirement |
|-------|-------------------|
| `OrdersAndRevenue(dateRange)` | Business: "see orders and revenue" |
| `DeliveriesCompleted(dateRange)` | Business: "how many deliveries are being completed" |
| `DailySummary` | Business: "basic reporting" |

Reporting **owns no data**. It is the one module allowed to run **read-only** SQL across the other modules' schemas. Reporting is cross-cutting by nature, and building copies of the data for one restaurant would be solving a problem we don't have.

## 3. Where data is stored

**One managed PostgreSQL database with one schema per module.** Each module is the *only* writer to its own schema.

| Schema | Owner module | Main tables |
|--------|--------------|-------------|
| `identity` | Identity | `users` (email, password hash, role, restaurant_id) |
| `customers` | Customers | `customer_profiles`, `addresses` |
| `restaurants` | Restaurants | `restaurants` (name, is_open, delivery_fee), `menu_items` (name, price, is_available) |
| `ordering` | Ordering | `orders`, `order_lines` (**snapshot** of item name + price at order time), `order_status_history` |
| `payments` | Payments | `payments` (order_id, amount, provider_reference, status) |
| `delivery` | Delivery | `drivers`, `driver_shifts`, `deliveries` |
| `notifications` | Notifications | `notification_log` (what was sent and when, for support) |
| *(none)* | Reporting | reads the others, read-only |

**Data ownership rules**
1. A module **writes only to its own schema**. Nobody else touches `ordering.orders`. Changes go through Ordering's slices or public API.
2. Modules reference each other **by ID only**. We use **no foreign keys across schemas** (for example `ordering.orders.customer_id` is not a foreign key to `customers`). This keeps modules separable later.
3. Where a module needs another module's data *as it was at a moment in time*, it **copies a snapshot**. An order line stores the item name and price at the time of ordering, so a later menu price change can't rewrite history.
4. **Card numbers are never stored and never pass through our servers.** The payment provider's hosted card fields turn the card into a token in the browser, and we only ever see that token.

Backups: the managed database's automated daily backups plus point-in-time recovery. This comes with the hosting and costs no extra work.

## 4. How components communicate

| From → To | How | Why this way (Q1) |
|-----------|-----|--------------------|
| Web client → Application | **HTTPS + JSON (REST)**, token in header | Simple, universal, works for web and any future mobile app. |
| Customer tracking an order | **Client polls** `GET /orders/{id}` every ~15 s | No real-time infrastructure needed. One restaurant means tiny load (D8). |
| Restaurant dashboard / driver view | Poll for new orders/requests **+** push notification as a nudge | Polling is the source of truth, so a lost push never loses an order. |
| Module → Module (commands: "do this") | **Direct in-process call to the other module's public API** (one interface per module) | No network, no broker, a full stack trace, easy to debug. |
| Module → Module (facts: "this happened") | **In-process domain events** (for example `DeliveryCompleted` → Ordering marks the order delivered) | Avoids two modules calling each other in a circle (Ordering → Delivery → Ordering). The receiver subscribes and the sender doesn't know who listens. Runs in memory in the same request, so there is no infrastructure (D5). |
| Module → its database | Its own data access code, its own schema only | Ownership rule above. |
| Payments → payment provider | **HTTPS through a `PaymentProvider` adapter** with a timeout | Isolates the external system (see section 5). |
| Notifications → push/email provider | **HTTPS through a `Notifier` adapter** | Same isolation idea. |
| Browser → payment provider | **Provider's hosted card fields** (e.g. Stripe Elements) | Card data never touches us, which keeps us out of most PCI compliance work. |

**Rule for communication between modules:** a module may only use another module's **public API file** (for example `ordering/OrderingApi.ts`) or subscribe to its **published events**. It may never import another module's slices, its domain classes or its tables. An automated **architecture test** in the build fails if someone breaks this rule (D3).

### Walkthrough: the main flow ("customer places an order")
1. The browser turns the card into a token through the provider's hosted fields.
2. `POST /orders` with basket + address + payment token → the **`PlaceOrder`** slice (Ordering).
3. PlaceOrder calls `Restaurants.getPricedItems(...)` and **recalculates the total on the server**. Prices from the client are ignored.
4. It saves the order as `PLACED`.
5. It calls `Payments.authorise(orderId, total, token)` → the adapter calls the provider (10 s timeout).
   - Success → the order becomes `AWAITING_ACCEPTANCE` and Notifications pings the restaurant.
   - Declined / error → the order becomes `PAYMENT_FAILED` and the customer is asked to try again.
6. The restaurant presses **Accept** → `AcceptOrder` → `Payments.capture(orderId)` → order `ACCEPTED`.
   Or **Reject** → `RejectOrder` → `Payments.void(orderId)` → order `REJECTED`, and the customer is never charged.
7. `MarkPreparing` → `MarkReadyForPickup` → Ordering calls `Delivery.requestDelivery(...)`.
8. A driver accepts → picks up (`DeliveryPickedUp` event → order `OUT_FOR_DELIVERY`) → delivers (`DeliveryCompleted` event → order `DELIVERED`).

## 5. External systems

| External system | Used for | How it is isolated | What happens if it fails (Q1) |
|-----------------|----------|--------------------|-------------------------------|
| **Payment provider** (Stripe or similar) | Card tokenising, authorise/capture/void/refund | Only the **Payments module** knows it exists. The rest of the code depends on our own `PaymentProvider` interface, and a `StripePaymentProvider` adapter translates our calls into Stripe's API. Swapping provider = writing one new adapter. | Calls time out after 10 s. A timeout or error is treated as "payment failed, please try again". If the customer is unsure, staff check the provider dashboard. **Known weakness** at scale: risk R2, fixed in Q3. |
| **Push / email provider** (e.g. Firebase Cloud Messaging, an email API) | Telling restaurants about new orders, customers about status, drivers about requests | Only the **Notifications module** knows it exists, behind a `Notifier` interface. | The failure is logged and the main operation still succeeds. The order is already saved, and screens poll, so nothing is lost. |
| **Hosting platform** (managed PaaS + managed PostgreSQL) | Running the app and the database | Not isolated in code. It is an operational choice (D9). | The platform's uptime. We rely on its health checks, restarts and backups. |

**Why isolate external systems this way?** This is the **Ports and Adapters** idea used pragmatically, *only at the edges where it pays*. Our code depends on an interface we own (the "port"). The vendor-specific code (the "adapter") sits behind it. We get:
- the vendor's model (their field names, their status codes) never leaking into Ordering;
- tests that use a fake provider;
- one place to add timeouts, logging and, later, retries and circuit breakers (Q3).

## 6. The major boundaries chosen

| # | Boundary | What sits on each side | Why it is there |
|---|----------|------------------------|-----------------|
| B1 | **Deployment boundary** | Everything is inside **one** deployable app. External providers are outside. | 3 devs, no DevOps: one thing to build, deploy, monitor and debug (D1). |
| B2 | **Module boundaries** (8 business capabilities) | Each module = its code + its schema + its public API. | Lets 3 people work in parallel without tangling, and lets any module be pulled out later without a rewrite. Ownership of each piece of data is unambiguous. |
| B3 | **Slice boundaries** (inside a module) | One folder per use case: endpoint, request/response, handler, data access. | A change to "accept order" touches one folder. New features add folders instead of editing shared layers (D2). |
| B4 | **External/integration boundary** | Our model ↔ the provider's model, via adapters. | A provider problem or change stays inside Payments/Notifications. |
| B5 | **Trust boundary** | Browser ↔ API. | The client is never trusted for prices, totals or roles. Card data never crosses into our system. |

### How the code inside each module is structured (vertical slices)

```
src/
  modules/
    ordering/
      OrderingApi.ts                <- PUBLIC: the only thing other modules may call
      events.ts                     <- PUBLIC: events this module publishes (OrderAccepted, ...)
      domain/
        Order.ts                    <- shared inside the module: state machine + invariants
      features/                     <- ONE FOLDER PER USE CASE (the vertical slices)
        place-order/
          PlaceOrder.endpoint.ts    <- HTTP route
          PlaceOrder.handler.ts     <- the use case, start to finish
          PlaceOrder.request.ts     <- input + validation
          PlaceOrder.test.ts
        accept-order/
        reject-order/
        track-order/
        list-my-orders/
        ...
      data/
        schema.sql                  <- the 'ordering' schema (owned only by this module)
    payments/
      PaymentsApi.ts
      features/ authorise/  capture/  void/  refund/
      provider/
        PaymentProvider.ts          <- port (our interface)
        StripePaymentProvider.ts    <- adapter (vendor code lives ONLY here)
    restaurants/ ... delivery/ ... (same shape)
  shared/                           <- tiny: auth middleware, db connection, event dispatcher, Money type
```

**The pragmatic part.** We follow the *spirit* of vertical slice architecture (organise by feature, minimise coupling *between* slices, each slice chooses its own complexity) but not its strictest form:
- **Shared domain model per module.** The `Order` state machine is shared by all Ordering slices, because "can an order go from PREPARING back to AWAITING_ACCEPTANCE?" must have exactly one answer.
- **Simple slices stay simple.** `ListMyOrders` is just a SQL query returning a DTO, with no domain class, no repository and no mapping layers. `PlaceOrder` uses the `Order` class because it has real rules.
- **Interfaces only where there is a real reason**: at external systems (payment provider, notifier) and at module public APIs. We do *not* put an interface in front of every repository "just in case".

> **Changed from earlier material:** the earlier answers used *Pragmatic Layered (presentation / application / domain / infrastructure folders) inside each module*. Layered splits a single feature across four folders (horizontal), so every feature touches every layer and developers collide in the same files. Vertical slices keep a feature in one folder. See `CHANGES-AND-REASONS.md` C1 and DECISION-LOG D2.

---

# Part 2 - What you identify

## 3 important architectural decisions

### Decision 1 - Modular monolith (one deployable, one database, schema per module)
- **What:** One application and one PostgreSQL database. Hard internal module boundaries: own schema, public API only, enforced by an architecture test.
- **Why now:** 3 developers, no DevOps, tiny budget, and the main risk is *the business model*, not scale. A monolith is the cheapest thing to build, run and change. The **modules** keep it from becoming a "big ball of mud".
- **Alternatives rejected:** *Microservices.* They need service discovery, many pipelines, distributed debugging and network failure handling, which is 5-10 deployables for 3 people. *Unstructured monolith.* Fast for 3 months, painful forever after, and impossible to split later.
- **Cost we accept:** we can't scale or deploy modules independently. We don't need to yet.
- Full detail: DECISION-LOG **D1**.

### Decision 2 - Vertical slices inside each module (pragmatic VSA)
- **What:** Code inside a module is grouped by **use case** (one folder per slice), not by technical layer. Each module keeps one small shared domain model where real business rules live.
- **Why now:** Each brief requirement *is* a slice, so the code mirrors the requirements. Adding or changing a feature touches one folder. Simple reads stay simple and complex writes get a domain model. That keeps ceremony low for a 3-person team.
- **Alternatives rejected:** *Layered / Clean Architecture* (organised by layer, so one feature spreads over 4 folders, with heavy interface ceremony), *full Hexagonal everywhere* (ports for everything), *full DDD tactical patterns everywhere* (overkill for CRUD-ish modules like Customers). We **borrow** from each where it pays: ports & adapters at external systems, DDD's aggregate idea for `Order`.
- Full detail: DECISION-LOG **D2**.

### Decision 3 - Payment: authorise at checkout, capture on accept, provider behind an adapter
- **What:** When the customer places an order we place a **hold** (authorisation) on their card. When the restaurant **accepts** we **capture** it. If the restaurant **rejects** we **void** the hold. The provider sits behind a `PaymentProvider` port in the Payments module, and the card is tokenised by the provider's hosted fields.
- **Why:** The customer only pays for orders that will actually be cooked, and we know the card is good *before* bothering the restaurant. A rejected order costs no refund (a void is free and instant). Card data never touches us.
- **Alternatives rejected:** *Charge immediately* (needs a refund for every rejection, and refunds take days and cost fees). *Charge after the restaurant accepts* (the earlier decision: the restaurant starts cooking before we know the card works, and the customer may have left the app when the charge fails).
- Full detail: DECISION-LOG **D6**. *(Changed from earlier material, see CHANGES C2.)*

> Other decisions recorded in the log: drivers belong to restaurants (D7), polling instead of WebSockets (D8), managed PaaS hosting (D9), in-process events (D5).

## 3 risks in the architecture

### Risk 1 - Module boundaries erode over time
- **What could happen:** Under deadline pressure someone queries another module's table directly ("it's the same database, it's just one JOIN"). Six months later the modules are tangled, and Q2/Q3-style changes become rewrites.
- **Why our design is exposed:** the shared database makes cheating *easy*.
- **Mitigation:** a schema per module, public-API-only access, an **architecture test in the build** that fails on illegal imports, no cross-schema foreign keys, and code review.

### Risk 2 - The payment provider is a single, slow, external dependency on the order path
- **What could happen:** The provider is slow or times out during `PlaceOrder`. A timeout *might* hide a successful authorisation. The customer sees "failed", tries again, and ends up with **two holds**. Because the call is synchronous, a slow provider also makes our order requests slow.
- **Why we accept it now:** with 1 restaurant and a small customer base this is rare and **can be fixed by hand** (staff void the duplicate hold in the provider dashboard). Building automatic reconciliation now would be solving a problem we haven't been given.
- **Watch for:** support tickets about double holds, and provider latency. **This is exactly what Q3 stresses.**

### Risk 3 - One process, one database: everything shares the same resources
- **What could happen:** One expensive thing (a heavy report, a slow notification send, a spike in browsing) slows *everything*, because it all runs in the same process against the same database. A bad deploy takes down the whole platform.
- **Why we accept it now:** load is tiny, and the managed platform gives health checks, restarts and backups.
- **Mitigation now:** sensible indexes, timeouts on external calls, notifications sent *after* the order is saved (a failure can't undo the order), basic monitoring/alerts from the hosting platform.
- **Watch for:** slow endpoints, high database CPU. **This is exactly what Q2 reports.**

## 2 things deliberately not done

### Not done 1 - Microservices, and any message broker or queue
- **Why not:** Microservices solve problems of *large teams* and *independent scaling*. We have 3 developers and 1 restaurant. They would multiply deployments, failure modes (every call becomes a network call that can fail) and cost. A message broker is another server to run with no DevOps team.
- **What we did instead:** module boundaries strong enough that a module *could* be extracted later, and in-process events that can later be made durable (Q2 does exactly that).
- **What would make us reconsider:** different modules needing very different scaling or deployment schedules, or several teams blocking each other.

### Not done 2 - Real-time driver GPS tracking (and real-time push channels in general)
- **Why not:** Live maps need continuous location streams, WebSockets, a map provider and battery-friendly mobile code. That is a lot of work and cost, and it doesn't prove the business model. Status-based tracking ("Preparing → Out for delivery → Delivered") meets the brief's "track the order".
- **What we did instead:** status updates, with clients polling every ~15 s.
- **What would make us reconsider:** customer complaints / support load about "where is my food", or competitors making it table-stakes.

**Also deliberately not done (smaller):** native iOS/Android apps (one responsive web app/PWA instead), ratings/reviews (one restaurant has nothing to be compared with), a separate reporting database, caching, multi-city support.

---

# Part 3 - The "questions to consider", answered directly

| Question | Answer |
|----------|--------|
| Monolith or multiple services? | **Modular monolith.** One deployable for 3 devs with no DevOps, plus hard module boundaries so it can evolve. |
| What are the major modules? | Identity, Customers, Restaurants (incl. menu), Ordering, Payments, Delivery, Notifications, Reporting. |
| Who owns each responsibility? | Each brief requirement is a slice in exactly one module (section 2 tables). |
| Who owns each piece of data? | Each module owns its own schema and is its only writer (section 3). Reporting owns nothing and reads read-only. |
| How tightly should modules be coupled? | **Loosely.** Only through public APIs (commands) and events (facts). No shared tables, no cross-schema foreign keys, IDs only. **Tight inside a module** (slices share the module's domain model), **loose between slices** of different modules. |
| How should business logic be organised? | By use case (vertical slices). Real business rules (the order state machine, pricing) live in a small shared domain model per module. Trivial reads stay as plain queries. |
| How should external systems be isolated? | Behind ports & adapters, inside the one module that needs them (Payments, Notifications). Card data is tokenised by the provider in the browser. |
| What happens if an external dependency fails? | Payment: timeout → the order is marked payment failed and the customer can retry. This is a known weakness (R2) handled by hand at this scale. Notifications: logged, and the main action still succeeds because screens poll. Hosting/DB: platform restarts + backups. |
| How should the code inside each module be structured? | Vertical slices (one folder per use case) + module public API + a small shared domain where needed (section 6). |

### Which internal architecture style, and why (the five options in the question)

| Option | Used? | How |
|--------|-------|-----|
| **Vertical Slice Architecture** | **Yes, primary** | Code organised by use case inside each module. |
| **Hexagonal / Ports & Adapters** | **Partly** | Only at the edges where it pays: payment provider and notification provider. |
| **Domain-Driven Design** | **Partly** | *Strategic* DDD gives us the module boundaries (bounded contexts). *Tactical* DDD (an aggregate with invariants) is used only for `Order`, where the rules are real. |
| **Clean Architecture** | No | Strict concentric layers + interfaces everywhere = too much ceremony for 3 devs, and it splits features horizontally. |
| **A-Frame** | Idea borrowed, not the structure | A-Frame keeps decision logic separate from infrastructure. We do this where it matters by keeping the `Order` state machine as pure code with no database or HTTP inside, so it is easy to test. We don't adopt the full A-Frame layout. |

Full comparison in [`Research.md`](Research.md) and DECISION-LOG **D2**.
