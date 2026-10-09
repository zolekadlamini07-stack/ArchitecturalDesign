# Question 1 - Research Notes (concepts behind the answer)

> Purpose: understand the ideas well enough to **explain and defend** them out loud.
> Deeper background still lives in the root folder: `05-DeploymentTopologyResearch.md` (monolith vs microservices vs modular monolith) and `06-InternalArchitectureResearch.md` (layered, clean, hexagonal, VSA). Where those documents recommend *Pragmatic Layered*, this folder supersedes them with *Pragmatic Vertical Slices* (see `../CHANGES-AND-REASONS.md` C1).

---

## 1. Two separate questions: deployment topology vs internal structure

People mix these up, so separate them clearly when presenting:

| Question | Answers it | Our choice |
|----------|-----------|------------|
| **How many deployable things are there, and how do they talk?** (deployment topology) | Monolith, modular monolith, microservices | **Modular monolith** |
| **How is the code organised inside a deployable/module?** (internal architecture) | Layered, Clean, Hexagonal, Vertical Slice, A-Frame, DDD tactical patterns | **Vertical slices** (pragmatic) |

The two combine: *modules* are the big boxes and *slices* are the folders inside each box.

```
Modular monolith  = vertical cut by BUSINESS CAPABILITY (Ordering, Payments, ...)
Vertical slices   = vertical cut by USE CASE inside each capability (PlaceOrder, AcceptOrder, ...)
```
Both cut **vertically** (by business meaning), never horizontally (by technical layer). That consistency is the reason the two fit together so well.

---

## 2. Modular monolith

**Definition.** One deployable application, internally divided into modules that behave *almost* like separate services: each has its own data, its own public API and hidden internals. The only difference from microservices is that the modules share a process (and here a database server) instead of talking over a network.

**What makes it "modular" and not just a monolith (the rules):**
1. **Each module owns its data.** Schema per module, and only the owner writes.
2. **Each module exposes a public API** (a facade/interface) and **events**. Everything else is internal.
3. **No reaching in.** No importing another module's classes, no querying another module's tables.
4. **Rules are enforced automatically**, for example by an architecture test (ArchUnit for Java, NetArchTest for .NET, dependency-cruiser or eslint-plugin-boundaries for TypeScript) that fails the build on an illegal import.

**Why it suits Q1:** one build, one deploy, one log stream, one database to back up. In-process calls can't suffer network failures. Yet the code is already shaped as if it were services, so extraction later is a *move*, not a *rewrite*.

**Classic failure mode:** the "big ball of mud", where boundaries exist only in folder names. Rule 4 is the defence.

**Key sources:** Simon Brown, "Modular Monoliths" talk; Kamil Grzybek, *modular-monolith-with-ddd* (GitHub); Shopify engineering blog on their modular monolith ("Deconstructing the Monolith").

---

## 3. Vertical Slice Architecture (VSA)

**Definition (Jimmy Bogard, 2018):** organise code around **features/requests** instead of technical layers. *"Minimise coupling between slices, maximise coupling within a slice."*

### Horizontal (layered) vs vertical (slices)

```
LAYERED (what the earlier answers had)          VERTICAL SLICES (what we have now)

 presentation/  OrderController  <- touched     features/
 application/   OrderService     <- touched       place-order/   endpoint + handler + request + test
 domain/        Order            <- touched       accept-order/  endpoint + handler + request + test
 infrastructure/OrderRepository  <- touched       track-order/   endpoint + query + test
                                                   ...
 One feature = edits in 4 folders.              One feature = edits in 1 folder.
 Every feature touches the same 4 big files.    Features rarely touch each other's files.
```

### What a slice contains
Everything needed for one request, from HTTP to database: the endpoint, the request/validation, the handler (the actual use case) and its data access, plus its tests.

### Why it is "pragmatic" for us (where we bend the textbook)
Strict VSA says each slice should be fully independent and duplication is acceptable. We bend it in three deliberate places:

| Strict VSA | Our pragmatic version | Reason |
|-----------|----------------------|--------|
| No shared domain; each slice owns its logic | **One small shared domain model per module** (e.g. `Order` with its state machine) | Business rules like "you can't accept a rejected order" must have one home, or slices will disagree. |
| Each slice talks to the DB however it likes | Slices use the module's **own schema only**, with module boundaries above slices | Modular-monolith ownership rules win over slice freedom. |
| No interfaces/abstractions | **Interfaces only at external systems and module APIs** | That's where swapping/testing/failure isolation actually pay off. |

**Each slice picks its own complexity:** `ListMyOrders` = one SQL query → DTO. `PlaceOrder` = validation + pricing + domain object + payment call. Layered architecture forces both through the same 4 layers. VSA doesn't.

**Typical VSA tooling:** a mediator / request-handler pattern (MediatR in .NET, or a simple `handle(request)` function per slice in TypeScript). Optional, since a plain function per slice works too.

**Risks of VSA (be ready for this question):**
- *Duplication between slices.* Accept small duplication and extract only when the same *business rule* repeats, which belongs in the domain model.
- *Inconsistency between slices.* Mitigate with conventions (same folder shape per slice) and code review.
- *Junior developers may find "where does logic go?" less obvious than in layers.* Mitigate with a slice template.

**Key sources:** Jimmy Bogard, "Vertical Slice Architecture" (jimmybogard.com, 2018) and NDC talk; Derek Comartin (CodeOpinion), "Restructuring to a Vertical Slice Architecture"; Milan Jovanović, articles on VSA + modular monolith.

---

## 4. The other styles in the question, and what we borrow

| Style | Core idea | Borrowed? |
|-------|-----------|-----------|
| **Layered** | Presentation → business → data, top-down dependencies. | No (horizontal). |
| **Clean Architecture** (Robert C. Martin) | Concentric circles. Dependencies point inward to entities. Use cases in the middle, frameworks at the edge. Interfaces at every boundary. | No as a structure. It is still layered and ceremony-heavy for 3 devs. |
| **Hexagonal / Ports & Adapters** (Alistair Cockburn) | The application core defines **ports** (interfaces). The outside world plugs in via **adapters**. The core never depends on technology. | **Yes, at the edges**: `PaymentProvider` port + `StripePaymentProvider` adapter, and `Notifier` port + push adapter. |
| **A-Frame** (James Shore) | Split *logic* (pure decisions) from *infrastructure* (I/O). A thin *application* layer coordinates them, so logic is testable without mocks. | **The idea**: the `Order` state machine is pure code with no I/O, and the slice handler does the I/O around it. |
| **DDD - strategic** (Eric Evans) | Bounded contexts: each part of the business has its own model and language. | **Yes**: modules = bounded contexts. "Order" in Ordering ≠ "delivery job" in Delivery. |
| **DDD - tactical** | Aggregates, entities, value objects, repositories, domain events. | **Selectively**: `Order` is an aggregate (guards its state transitions), `Money` is a value object, and domain events flow between modules. CRUD modules (Customers) don't use it. |

**Ports and adapters, in one example**
```ts
// payments/provider/PaymentProvider.ts   <- PORT (ours, speaks our language)
export interface PaymentProvider {
  authorise(input: { reference: string; amount: Money; paymentToken: string }): Promise<AuthResult>;
  capture(providerPaymentId: string): Promise<void>;
  void(providerPaymentId: string): Promise<void>;
}

// payments/provider/StripePaymentProvider.ts   <- ADAPTER (the only file that knows Stripe)
export class StripePaymentProvider implements PaymentProvider {
  async authorise({ reference, amount, paymentToken }) {
    const intent = await stripe.paymentIntents.create(
      { amount: amount.cents, currency: amount.currency, payment_method: paymentToken,
        capture_method: 'manual', confirm: true, metadata: { reference } },
      { timeout: 10_000 });
    return intent.status === 'requires_capture'
      ? { status: 'AUTHORISED', providerPaymentId: intent.id }
      : { status: 'DECLINED', reason: intent.last_payment_error?.code };
  }
  ...
}
```

---

## 5. Authorise-then-capture (card payments)

Card payments can be split into two steps:
1. **Authorisation**: the bank confirms the card is valid and *reserves* the amount (a "hold"). No money moves yet.
2. **Capture**: we claim the reserved money. It must happen within the provider's window (typically ~7 days).
3. **Void** (cancel authorisation): we release the hold. The customer is never charged. Free and instant.
4. **Refund**: return money *after* capture. Slower (days) and may cost fees.

**Why it matches a food platform:** the restaurant can still reject, so we hold at checkout and capture on accept. A rejection is a void, not a refund. Stripe calls this `capture_method: manual` on a PaymentIntent. Most providers support it.

**PCI DSS in one line:** if card numbers ever touch your servers, you inherit heavy security compliance. Hosted fields (Stripe Elements, Adyen Drop-in, etc.) mean only the provider sees the card, and we get a token.

---

## 6. In-process domain events

A **domain event** is a fact in past tense: `OrderAccepted`, `DeliveryCompleted`. The module that owns the fact publishes it, and other modules subscribe.

- In Q1 the "event bus" is just **an in-memory dispatcher**: `publish(event)` calls each registered handler, in the same process and request.
- Why bother in a monolith? It **breaks circular dependencies** (Ordering calls Delivery to request a driver, and Delivery *publishes* `DeliveryCompleted` instead of calling Ordering back). It also keeps the sender ignorant of who reacts.
- **Weakness (accepted in Q1):** in-memory events are lost if the process crashes between saving data and running the handler. Q2 fixes this by making events durable (outbox + job queue).

---

## 7. Polling vs push for status updates

| Approach | How | Cost |
|----------|-----|------|
| **Polling** | The client asks `GET /orders/{id}` every N seconds | Zero infrastructure. Wastes some requests, which is irrelevant at 1 restaurant. |
| **Server-Sent Events / WebSockets** | The server pushes over a long-lived connection | Real-time, but needs connection management, sticky sessions or a backplane when scaled. |
| **Push notifications** | Through FCM/APNs to the device | Good for "nudges", but not guaranteed delivery, so never the source of truth. |

Q1 = polling as the source of truth + push notifications as nudges.

---

## 8. Glossary (quick recall before presenting)

| Term | Meaning in one line |
|------|---------------------|
| Bounded context | A part of the business with its own model and words; our modules. |
| Aggregate | A cluster of objects changed together through one root that protects the rules (`Order`). |
| Value object | An immutable thing defined by its value, e.g. `Money(1250, 'ZAR')`. |
| Port / Adapter | Our interface / the vendor-specific implementation of it. |
| Slice | All the code for one use case, in one folder. |
| Public API (module) | The only entry point other modules may call. |
| Snapshot | A copy of another module's data as it was at a moment (price at order time). |
| Architecture test | An automated test that fails the build if code breaks dependency rules. |
| Authorise / Capture / Void | Hold money / take the held money / release the hold. |
