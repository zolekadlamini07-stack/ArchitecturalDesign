# Final Answers - Food Delivery Platform Architecture (Deliberate Practice)

**Approach in one line:** a **modular monolith** (one deployable, one PostgreSQL database, a schema per module) with **pragmatic vertical slices** inside each module (one folder per use case). It evolves *by addition*: each change is tied to a problem the brief actually gives us.

---

## Folder map

```
FinalAnswers/
  README.md                  <- you are here: reading order, the story, pitches, likely questions
  PLAIN-ENGLISH-GUIDE.md     <- every technical term explained simply, with everyday analogies
  Architecture-Presentation.pptx <- black-and-white slide deck (31 slides, speaker notes included)

../Code/                     <- the architecture BUILT in C# / ASP.NET Core, one solution per phase
                                (Phase1-Q1, Phase2-Q2, Phase3-Q3 + challenge). Code/README.md maps
                                every concept in these documents to the file where you can see it.
  DECISION-LOG.md            <- every pattern/technology: what it is, options, pros/cons, why now (D1-D29)
  CHANGES-AND-REASONS.md     <- what changed from the earlier material, and the technical reason (C1-C14)
  Question1/  Question1.md   <- the answer, under each part of the question
              Research.md    <- concepts: modular monolith, VSA vs layered, ports & adapters, DDD, A-Frame...
              diagrams/      <- Q1-Architecture.drawio (+ .png): p1 system, p2 inside a module
  Question2/  Question2.md / Research.md / diagrams/ (p1 architecture, p2 flows)
  Question3/  Question3.md / Research.md / diagrams/ (p1 architecture, p2 state machines, p3 failure sequence)
  Challenge/  Challenge.md / diagrams/ (integration via an anti-corruption layer module)
```
Open `.drawio` files at <https://app.diagrams.net> or in draw.io desktop. The `.png` files next to them are ready to show. **The architecture page uses the same layout in Q1, Q2 and Q3**, so flipping between `Q1-Architecture-page1.png` → `Q2-...` → `Q3-...` shows the evolution. Dashed border = new at that stage, thick border = changed at that stage.

---

## The whole story on one page

| | **Q1** - prove the model | **Q2** - growing pains | **Q3** - Friday night outage | **Challenge** - acquisition |
|---|---|---|---|---|
| Context | 1 restaurant, 3 devs, no DevOps, tiny budget | 15→50 restaurants, 5 devs, 1 city | 50 restaurants, 3 cities, 12 devs / 2 teams | Acquired company with its own systems |
| Topology | Modular monolith | **Same** + worker process | **Same** (worker queues split) | **Same** + one new module |
| Inside modules | Vertical slices | Same | Same | Same |
| Key additions | Ports & adapters at payment/push providers, authorise→capture, schema per module, architecture tests | Redis cache, **outbox + PostgreSQL job queue**, worker, read replica, rolling deploys, DB roles | Order-first, **payment ledger with UNKNOWN**, idempotency keys, circuit breaker, bulkhead, **reconciliation** | **Anti-Corruption Layer module**, ID mapping, synced catalogue, contract tests |
| Deliberately NOT | Microservices, broker, GPS | Microservices, broker, Kubernetes, search engine | Extracting Payments, 2nd provider, 2PC, event sourcing | Shared DB, rewrite, payment merge |
| The lesson | Simplest thing with **strong boundaries** | **Fix symptoms with targeted additions**, not a redesign | A timeout means **unknown**, not failed | **Translate at the edge**; each side owns its data |

**The thread that ties it together:** Q1's module boundaries are *why* every Q2 change touches only one or two modules. Q2's outbox is *why* Q3 can guarantee a successful payment is never lost. Q3's resilience patterns are *reused unchanged* for the acquired platform.

---

## Suggested reading order (≈ 2 hours of prep)

| Time | Read | Goal |
|------|------|------|
| 20 min | `Question1/Question1.md` + diagram pages 1-2 | Be able to draw the Q1 diagram from memory and explain *slices vs layers*. |
| 15 min | `Question1/Research.md` sections 1-4 | Defend "why modular monolith" and "why VSA" in your own words. |
| 20 min | `Question2/Question2.md` + diagram | Map each of the 7 problems to its fix. Explain the **dual-write problem**. |
| 30 min | `Question3/Question3.md` sections 1, 3, 5 + diagram pages 2-3 | Walk through the 7 failure scenarios without notes. **This is the hardest part.** |
| 15 min | `Challenge/Challenge.md` sections 1-4 + diagram | Explain the ACL and who owns what. |
| 15 min | `DECISION-LOG.md` index + the entries you're least sure about | Have alternatives ready for every choice. |
| 5 min | `CHANGES-AND-REASONS.md` summary table | Know what moved since earlier drafts and why. |

---

## 30-second summaries (say these out loud)

**Q1.** "Three developers, no DevOps, one restaurant, so we build one deployable: a modular monolith. It has eight modules that each own their own schema in one PostgreSQL database and talk only through a public API or events. Inside each module, code is organised by use case as vertical slices, so each requirement in the brief is a folder. External systems like the payment provider sit behind adapters. We authorise the card at checkout and capture when the restaurant accepts, so rejections never need refunds. The risks we accept are boundary erosion, the payment provider being on the critical path, and everything sharing one process and database. Those are exactly the things Q2 and Q3 test."

**Q2.** "We keep the architecture, because none of the symptoms is caused by being a monolith. Each symptom gets a targeted fix. Browsing gets a Redis cache. Notifications move off the request path into a worker, via a transactional outbox and a PostgreSQL job queue, which means no new server and no lost messages. Reporting moves to a read replica. Deploys become rolling on two instances with backward-compatible migrations. Module ownership is enforced by database roles and CODEOWNERS. We deliberately don't add microservices, a message broker or Kubernetes."

**Q3.** "The root mistake was treating a payment as success or failure. Over a network it can also be *unknown*. So the order is created first, as payment-pending. Payments records every attempt before calling the provider and sends an idempotency key, so retries can't double-charge. It calls the provider only from a worker, behind a circuit breaker. A timeout becomes UNKNOWN, never failed. Webhooks, a sweeper and a daily settlement check resolve every unknown. Duplicate clicks return the same order. Crashes are safe because results and events are saved in one transaction. And if money was held for an order we gave up on, we void it automatically."

**Challenge.** "We add one module, an anti-corruption layer. It's the only code that knows the acquired platform exists. Their restaurants are synced into ours as read-only copies. Orders our customers place with them are forwarded with our order ID as the idempotency key, and their status webhooks are translated back into our own order commands. Each platform keeps owning its own data and its own payments. Failures on their side are contained with the same circuit breaker, retry and reconciliation tools as payments, and their API version is pinned and covered by contract tests."

---

## Likely questions and short answers

| Question | Short answer | Where |
|----------|--------------|-------|
| Why not microservices? | They solve team-scale and independent-scaling problems we don't have, and add network failures, pipelines and cost. Our modules can be extracted later. | D1, Q1 Not-done 1 |
| Isn't vertical slicing going to cause duplication? | Some, deliberately. Real business rules live once in a small per-module domain model. Only incidental code repeats. | D2, Q1 Research §3 |
| How do you stop the modules turning into a big ball of mud? | Schema per module, public API only, architecture tests in CI, DB roles per module, CODEOWNERS. | D3, D17 |
| Why PostgreSQL as a queue instead of RabbitMQ? | It's transactional with our data (no dual-write) and needs no new server. Enough for our volume. RabbitMQ would still need an outbox. | D12, CHANGES C7 |
| What if PostgreSQL becomes the bottleneck? | Watch queue latency and DB CPU. Move the queue to a broker *behind the same outbox*; the module code doesn't change. | D12 |
| Why not extract Payments in Q3? | The incident is external ambiguity. Extraction adds another network boundary with the same ambiguity. Payments is extraction-ready if team/compliance needs arise. | D23, CHANGES C9 |
| How do you prevent duplicate payments? | Client idempotency key + one pending order per customer + one attempt per order + the provider idempotency key. | Q3 §5.3-5.4, D20 |
| What if payment succeeds but order creation fails? | Can't happen: the order is created first and payment is triggered from it. If the order is later abandoned, the hold is voided. | Q3 §5.5 |
| How do you know what happened after the fact? | Append-only `payment_events`, `order_status_history`, correlation id = order id, support timeline view, daily settlement reconciliation. | Q3 §5.8, D24 |
| Why an ACL instead of a shared database? | A shared DB couples both schemas forever and breaks data ownership. The ACL isolates their model and their failures in one module. | D26 |

---

## Where the old material fits

- Root files `00-06*.md`, `Question1/2/3-Answers.md`, `Question2/`, `Question3/` are kept as **history and deep research**. Each answer file now has a banner pointing here.
- Where they conflict with this folder (layered vs slices, RabbitMQ, extracted Payments, payment timing), **this folder wins**. Every such change is explained in `CHANGES-AND-REASONS.md`.
- `diagrams/01-05` (user flows, system flowchart, ERD) are still useful references, except that payment now happens at checkout (authorise), not after acceptance.
