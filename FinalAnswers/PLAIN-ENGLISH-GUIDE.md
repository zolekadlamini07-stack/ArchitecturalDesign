# Plain-English Guide to Every Technical Term We Use

> **What this is:** every piece of jargon in the answers, explained simply. Each one has **what it is**, **an everyday analogy**, and **why we use it** in this project.
> Read Part 1 first. It tells the whole story in plain English, so the terms in Part 2 have somewhere to fit.

---

# Part 1: How it all works together (the story of one order)

Picture the whole platform as **one big restaurant building**.

- The building is the **application**. We have **one building**, not a chain of separate shops. That's the **monolith**.
- Inside, the building has **separate rooms with locked doors**: the kitchen, the till, the delivery desk, the office. Each room has its own staff and its own filing cabinet. That's what makes it a **modular** monolith. Each room is a **module**.
- Inside each room, every job has **its own folder of instructions** ("how to take an order", "how to accept an order"). Everything you need for that one job is in that one folder. That's **vertical slices**.
- All the filing cabinets live in **one storage room** (the **database**, PostgreSQL), but each room has its **own locked drawer** (a **schema**). Rooms can't open each other's drawers.
- Rooms talk to each other through a **hatch in the wall** (a **public API**). You hand a request through the hatch; you never walk into another room.

**Now follow one order (the final, Question 3 version):**

1. A customer opens the app and looks at menus. Menus are read constantly but rarely change, so we keep a **copy on a whiteboard by the door** (the **Redis cache**) instead of walking to the storage room every time.
2. The customer types their card into a box that actually belongs to the **payment company** (**hosted card fields**). We never see the card number, only a **token** that stands for it.
3. They press **Place Order**. Their phone attaches a **ticket number** (an **idempotency key**). If they press it twice, we see the same ticket number and say "you already ordered this". No second order.
4. We write the order down as **"waiting for payment"** and, **on the same page at the same time**, write a note saying "someone needs to take payment for this" (the **outbox**). Both are written together or not at all.
5. A **back-office worker** (the **worker process**) picks up that note from the **to-do pile** (the **job queue**) and phones the payment company. The customer doesn't wait on the phone. They see "Confirming your payment…".
6. Before calling, the worker writes in a **logbook** (the **payment ledger**): "calling about ticket A".
7. If the payment company **answers**, great. If the line **goes dead**, we don't assume "failed". We write **"don't know yet" (UNKNOWN)** and check later.
8. If the payment company keeps failing, a **safety switch** (the **circuit breaker**) stops us calling for a while so we don't jam their line or waste our staff's time.
9. Three ways we find out the truth later (**reconciliation**): they **call us back** (a **webhook**), we **ring them to check** (the **sweeper**), and every morning we **compare our logbook with their statement** (**settlement**).
10. Once payment is confirmed, the order goes to the restaurant. If we gave up waiting and the money was held anyway, we **release the hold** (a **void**).

Everything else in this guide is a detail of that story.

---

# Part 2: Every term, one at a time

## A. The big shape of the system

### Architecture
- **What it is:** the overall plan for how a software system is split up and how the parts talk to each other.
- **Analogy:** the floor plan of a building, before anyone picks the paint colours.

### Monolith
- **What it is:** one application that is built, deployed and run as a single unit.
- **Analogy:** one big shop under one roof.
- **Good:** simple to run, nothing to coordinate. **Bad:** if it's messy inside, it becomes a tangle that's hard to change.

### Modular monolith *(our choice)*
- **What it is:** still one application, but split inside into **modules** with strict walls between them. Each module owns its own data and only talks to others through a defined entry point.
- **Analogy:** one building with **separate rooms and locked doors**. One roof and one electricity bill, but tidy, well-organised rooms.
- **Why we use it:** 3 developers and no operations team means we can only manage *one* thing to run. The walls keep it organised so it doesn't become a tangle, and a room can be moved into its own building later if ever needed.

### Microservices *(deliberately not used)*
- **What it is:** splitting the system into many small, separate applications that talk over the network.
- **Analogy:** instead of one building, **a separate shop for every department**, each with its own staff, keys and delivery van, phoning each other all day.
- **Why not:** great for big companies with many teams, but for us it means many things to run, and every phone call between shops can fail.

### Module
- **What it is:** one self-contained business area inside the application (Ordering, Payments, Delivery…).
- **Analogy:** one room in the building, with its own staff and its own filing drawer.

### Our 8 modules, in one line each
| Module | Plain English |
|--------|---------------|
| Identity | Who you are and what you're allowed to do (customer, restaurant staff, driver, admin). |
| Customers | Your profile and delivery addresses. |
| Restaurants (incl. menu) | Restaurant details, open/closed, and their menus. |
| Ordering | The order itself, from "placed" to "delivered". The heart of the system. |
| Payments | The only part that talks to the payment company. |
| Delivery | Drivers, shifts and delivery jobs. |
| Notifications | Sends the "your food is on its way" messages. |
| Reporting | Numbers for the business: orders, revenue, deliveries. |

### Boundary
- **What it is:** a line you don't cross without permission, e.g. "Ordering may not read Payments' data directly".
- **Analogy:** a locked door between rooms. You knock at the hatch, you don't walk in.

### Coupling (tight vs loose)
- **What it is:** how much one part depends on another's insides.
- **Analogy:** **tight** = two people handcuffed together (one moves, the other must too). **Loose** = two people who agree to talk by text message (each can move freely).
- **We aim for:** loose between modules, and tighter inside a module where the code really belongs together.

---

## B. How code is organised inside each module

### Layered architecture *(the earlier draft; replaced)*
- **What it is:** code sorted by **technical type**: one folder for screens/endpoints, one for business logic, one for data, one for database code.
- **Analogy:** a kitchen where **all knives are in one drawer, all pans in another, all recipes on a shelf across the room**. To make one dish, you visit every drawer.
- **Problem:** one feature touches four folders, and everyone edits the same big files, so developers bump into each other.

### Vertical Slice Architecture (VSA) *(our choice)*
- **What it is:** code sorted by **feature / job**. Each feature ("place order", "accept order") gets its own folder with *everything* it needs, from the web endpoint down to the database query.
- **Analogy:** **meal kits**. Each box contains every ingredient and the recipe for one dish. To change one dish, you open one box.
- **Why we use it:** each requirement in the brief becomes one folder. Changing a feature means touching one place, and two developers working on two features rarely touch the same files.

### "Pragmatic" (what it means here)
- **What it is:** following the **spirit** of a method, not every rule to the letter. You bend the rules where bending them is clearly sensible.
- **Analogy:** a recipe says "use exactly 200 g of flour"; a pragmatic cook uses a cup because the cake comes out the same and it saves time.
- **Is our VSA pragmatic? Yes.** Strict VSA says slices should share nothing. We bend that in three places:
  1. **One shared rulebook per module.** All Ordering slices share the order's rules ("you can't accept a cancelled order"), so the answer is always the same.
  2. **Module walls come first.** A slice may only touch its own module's data drawer.
  3. **Extra layers only where they pay off.** We add a "plug socket" (interface) only in front of outside companies like the payment provider, not everywhere.

### Clean Architecture *(not used)*
- **What it is:** code arranged in rings: core business rules in the middle, databases and web on the outside, with strict rules about which ring may know about which.
- **Analogy:** an onion where the middle must never know the outer skins exist.
- **Why not:** lots of extra files and ceremony for a 3-person team, and it still sorts code by layer, not by feature.

### Hexagonal architecture / Ports and Adapters *(used only at the edges)*
- **What it is:** your code defines a **socket** (port) describing what it needs ("I need to take a payment"). Each outside company gets a **plug** (adapter) that fits that socket.
- **Analogy:** a **travel adapter**. Your laptop doesn't care which country's wall socket you use, because the adapter handles it.
- **Why we use it:** only Payments knows the payment company's details. Switching from Stripe to another provider means writing one new plug, not rewriting the system.

### Adapter
- **What it is:** the small piece of code that translates between our language and an outside system's language.
- **Analogy:** an interpreter at a meeting.

### A-Frame architecture *(idea borrowed)*
- **What it is:** keep **decisions** (pure logic) separate from **doing** (saving to the database, calling the internet).
- **Analogy:** a referee decides "that's a foul"; someone else walks over and writes it on the scoreboard. The referee can be tested without a scoreboard.
- **Where we use the idea:** the order's rules are plain logic, easy to test without a database.

### Domain-Driven Design (DDD) *(used selectively)*
- **What it is:** designing software around how the **business** talks and works, using the business's own words.
- **Analogy:** before building a hospital system, you learn how doctors and nurses actually speak and work, and mirror that.
- **Parts we use:** bounded contexts (our modules) and an aggregate (the Order).

### Bounded context
- **What it is:** an area where a word has one clear meaning. "Order" in Ordering means the customer's order; in Delivery, the same thing is "a delivery job".
- **Analogy:** "bat" means one thing at a cricket match and another at a zoo.

### Aggregate
- **What it is:** a group of data that must always be changed together, through one "guard" that enforces the rules.
- **Analogy:** a **bank teller**. You don't change your balance yourself; you ask the teller, who checks the rules first.
- **For us:** the Order is the guard of its own status changes.

### Value object
- **What it is:** a small thing defined only by its value, e.g. `Money(45.00, ZAR)`.
- **Analogy:** a R50 note. Any R50 note is as good as another.

### State machine
- **What it is:** a list of allowed statuses and which status may follow which.
- **Analogy:** a **board game track**. From "Preparing" you can move to "Ready", but you can't jump back to "Waiting for restaurant".

### Domain event (in-process event)
- **What it is:** an announcement that something has happened, e.g. "DeliveryCompleted". Whoever cares listens and reacts.
- **Analogy:** a **bell in the kitchen** that rings when food is ready. The chef doesn't need to know who's listening.
- **Why:** it stops two rooms calling each other back and forth. **In-process** means the bell only rings inside the building, in memory (which is why it can be lost if the power cuts, fixed in Q2).

### Public API (of a module)
- **What it is:** the one official entry point other modules may use.
- **Analogy:** the **serving hatch** between kitchen and dining room.

### Architecture test
- **What it is:** an automatic check that fails the build if code breaks the rules (e.g. Ordering reaching into Payments' insides).
- **Analogy:** a **smoke alarm for messy code**. It goes off before the mess spreads.

---

## C. Data and storage

### Database
- **What it is:** where the system permanently keeps its information.
- **Analogy:** the filing room.

### PostgreSQL
- **What it is:** a popular, free, very reliable database that stores data in tables (rows and columns), a bit like very strict spreadsheets.
- **Why we use it:** money and orders need correctness, and it's cheap to run as a managed service. Later it also doubles as our job queue.

### Schema (one per module)
- **What it is:** a named section inside the database that belongs to one module.
- **Analogy:** **one locked drawer per room** in the shared filing cabinet.

### Transaction
- **What it is:** a group of changes that either **all happen or none happen**.
- **Analogy:** a **bank transfer**: money must leave one account *and* arrive in the other. Never just one half.

### Snapshot
- **What it is:** copying a value as it was at a moment in time, e.g. the price of a burger on the order line.
- **Analogy:** a **receipt**. If the menu price changes tomorrow, your receipt still shows what you paid.

### Foreign key (and why we avoid them across modules)
- **What it is:** a database rule that links a row in one table to a row in another.
- **Why we avoid them across modules:** they would glue the drawers together, making it hard to separate modules later. We store just the ID number.

### Index
- **What it is:** a lookup structure that makes searching a table fast.
- **Analogy:** the **index at the back of a book**. You jump straight to page 214 instead of reading the whole book.

### Pagination
- **What it is:** returning results in pages (20 at a time) instead of everything at once.
- **Analogy:** Google's "page 1, 2, 3" of search results.

### N+1 query problem
- **What it is:** asking the database once for a list, then once more for *each* item on it, which adds up to lots of small trips.
- **Analogy:** going to the shop separately for each item on your grocery list instead of one trip with the whole list.

### pg_stat_statements
- **What it is:** a built-in PostgreSQL tool that records which queries are slow or run most often.
- **Analogy:** a **speed camera** for database queries. It tells you exactly where to fix, so you don't guess.

### Read replica
- **What it is:** a live, read-only copy of the database that stays a second or two behind the main one.
- **Analogy:** a **photocopy of the filing room that updates itself constantly**. The accountants read the copy, so they don't crowd the staff using the original.
- **Why we use it (Q2):** heavy reports run on the copy, so they stop slowing down orders.

### Replication lag
- **What it is:** the small delay before the copy catches up.
- **Analogy:** a live TV broadcast running a couple of seconds behind real life.

### Database roles
- **What it is:** separate logins for each module, each only allowed into its own drawer.
- **Analogy:** **key cards** that only open your own room.

---

## D. Talking between apps and users

### HTTPS / REST / JSON
- **What it is:** the standard way apps talk over the internet. **HTTPS** is the secure phone line, **REST** is the etiquette for asking ("GET this order", "POST a new order"), and **JSON** is the simple text format the answer comes back in.
- **Analogy:** ordering at a counter using a **standard order form**, over a **private line**.

### Polling
- **What it is:** the app asks "any update?" every few seconds.
- **Analogy:** a child in the back seat asking **"are we there yet?"** every 15 seconds. Simple and it works.
- **Why we use it:** no extra infrastructure, and at our size the extra questions don't matter.

### WebSockets *(not used yet)*
- **What it is:** an always-open connection so the server can push updates instantly.
- **Analogy:** **an open phone line** instead of calling back repeatedly. Faster, but more to manage.

### Push notifications (e.g. Firebase Cloud Messaging)
- **What it is:** a service that pops a message onto your phone.
- **Analogy:** a **doorbell**. Useful nudge, but if it doesn't ring, the information is still on the screen (polling), so nothing is lost.

### PWA (Progressive Web App)
- **What it is:** a website that can be "installed" on a phone and behave like an app.
- **Why we use it:** one codebase for web and phone, instead of building separate iPhone and Android apps.

---

## E. Money and payments

### Payment provider (e.g. Stripe)
- **What it is:** a company that handles card payments for us.
- **Analogy:** the **bank's card machine** at a shop.

### Authorise, capture, void, refund
| Term | Plain English | Analogy |
|------|---------------|---------|
| **Authorise** | Put a **hold** on the money. Nothing is taken yet. | A hotel holding a deposit on your card at check-in. |
| **Capture** | Actually **take** the held money. | Checking out and paying the bill. |
| **Void** | **Cancel the hold**. The customer is never charged. | The hotel releasing the deposit. |
| **Refund** | **Give back** money already taken. | Returning a shirt for your money back (slower, can cost fees). |

**Our rule:** hold at checkout, take when the restaurant accepts, release if they reject.

### Hosted card fields, tokenisation, PCI
- **What it is:** the card number box on our page actually belongs to the payment company. They send us a **token** (a stand-in code), never the card number. **PCI** is the strict security standard you must follow if you *do* handle card numbers.
- **Analogy:** a **coat-check ticket**. We hold the ticket; the coat (your card) stays with the coat-check.
- **Why:** if we never touch card numbers, we avoid most of the PCI compliance work.

### Payment attempt ledger
- **What it is:** our own notebook recording every payment attempt and every reply, **written before we call** the provider.
- **Analogy:** a **phone log**: "10:02 rang the bank about order 55… 10:02:10 line went dead".

### UNKNOWN state
- **What it is:** an honest status meaning "we asked and never heard back".
- **Analogy:** you posted a letter but got no reply. That doesn't mean it was lost, so you don't send the money twice; you check.

### Webhook
- **What it is:** the provider **calls us** to tell us what happened.
- **Analogy:** a **callback**: "I'll ring you back when it's done."

### Settlement report
- **What it is:** the provider's daily list of every payment.
- **Analogy:** your **bank statement**.

### Reconciliation
- **What it is:** comparing our records with the provider's and fixing any difference.
- **Analogy:** **checking your bank statement against your receipts** at month end.
- **Our three layers:** they call us (webhook), we call them (sweeper), and we compare statements daily (settlement).

### Sweeper
- **What it is:** a background job that regularly finds "don't know yet" payments and checks on them.
- **Analogy:** a **night cleaner** walking round every few minutes picking up anything left lying around.

### Compensation
- **What it is:** an action that undoes an earlier step when things go wrong (void, refund).
- **Analogy:** a shop **giving your money back** when they can't deliver.

### Saga
- **What it is:** a business process made of several steps, where each step has an "undo" if a later one fails.
- **Analogy:** booking a holiday: flight, then hotel, then car. If the car fails, you cancel the hotel and flight.

### Two-phase commit (2PC) *(not used)*
- **What it is:** a heavy method to make several separate systems commit a change at exactly the same moment.
- **Analogy:** a **wedding ceremony**: "Do you?" "I do." "Do you?" "I do." Only then is it official. Slow, and the payment company won't take part anyway.

---

## F. Doing work in the background

### Background job / worker process
- **What it is:** work done **after** the user gets their answer, by a separate program. Ours is the **same code** started in "worker mode".
- **Analogy:** a waiter takes your order and walks away; the **kitchen** cooks in the back. You don't stand in the kitchen waiting.
- **Why (Q2):** sending notifications was making customers wait. Now the worker sends them.

### Queue / job queue
- **What it is:** a list of jobs waiting to be done, in order.
- **Analogy:** the **ticket rail** in a kitchen where order slips hang until a chef takes one.

### PostgreSQL-backed job queue *(our choice)*
- **What it is:** keeping that ticket rail **inside our existing database** instead of buying a separate system. A feature called `SKIP LOCKED` lets several workers each grab a different ticket without fighting over the same one.
- **Why:** no new system to run, and jobs are saved together with our data.

### Message broker: RabbitMQ, Kafka *(not used)*
- **What it is:** a dedicated system just for passing messages between programs.
- **Analogy:** hiring a **full-time post office** for your building.
- **Why not (yet):** one more system to run, and it creates the dual-write problem below.

### Dual-write problem
- **What it is:** saving something in two different places as two separate steps. If you crash in between, one place has it and the other doesn't.
- **Analogy:** writing an appointment in your diary, then **meaning** to text your friend, but your phone dies first. Now the two of you disagree.

### Transactional outbox
- **What it is:** writing the message ("tell the restaurant") **in the same transaction** as the data ("save the order"), in an outbox table. A worker sends it afterwards.
- **Analogy:** writing the appointment **and** the "text my friend" note on **the same page in one go**. Even if your phone dies, the note is there when it comes back.

### Inbox
- **What it is:** a record of messages already received, so a repeat delivery gets ignored.
- **Analogy:** ticking off letters you've already read so you don't act on the same one twice.

### At-least-once delivery
- **What it is:** a message is **guaranteed to arrive**, but **may arrive twice**.
- **Analogy:** a courier who **always** delivers, but sometimes drops off a duplicate parcel.

### Idempotent / idempotency key
- **What it is:** **idempotent** = doing it twice has the same effect as doing it once. An **idempotency key** is a unique ticket number attached to a request so the receiver can say "I've seen this one".
- **Analogy:** a **lift button**. Pressing it five times still calls the lift once. Or a **cloakroom ticket number**: show it twice, still one coat.
- **Why:** customers double-click, messages arrive twice, and retries happen. None of these must charge twice.

### Retry, backoff, jitter
- **Retry:** try again after a failure.
- **Backoff:** wait **longer** each time (30 s, 1 min, 2 min…).
- **Jitter:** add a bit of **randomness** to the wait.
- **Analogy:** calling a busy friend. You don't redial every second; you wait longer each time. Jitter stops a thousand people all redialling at exactly the same moment.

### Dead-letter
- **What it is:** where a job goes after failing too many times, for a human to look at.
- **Analogy:** the post office's **"undeliverable mail"** box.

### Timeout
- **What it is:** a maximum time we'll wait for an answer (10 seconds for payments).
- **Analogy:** "If they haven't picked up after 10 rings, hang up."

### Circuit breaker
- **What it is:** after too many failures, we **stop calling** a broken service for a while, then try a few test calls. If those work, we resume. Three positions: **closed** (normal), **open** (stop calling), **half-open** (testing).
- **Analogy:** the **circuit breaker in your house**. When something shorts, it trips to protect the rest. After a while you flip it back and see if it holds.

### Bulkhead
- **What it is:** separate resources for separate jobs, so one problem can't use up everything.
- **Analogy:** the **watertight compartments in a ship**. One floods, the ship still floats. For us: payment jobs have their own lane, so a payment outage can't block notifications.

---

## G. Speed and scale

### Cache / Redis cache *(Q2)*
- **What it is:** a **cache** is a fast, temporary copy of data you read often. **Redis** is a very fast store that keeps data in memory (RAM) instead of on disk, so reading from it is extremely quick.
- **Analogy:** the **specials board by the door**. Instead of walking to the back office to read the menu file for every customer, you read the board. When the menu changes, you wipe and rewrite the board.
- **Why:** browsing menus was the busiest thing in the system. Most reads now come from the cache, not the database.
- **Safety rule:** the cache is only for **showing** menus. When charging, we always check the real price in the database.

### Cache-aside
- **What it is:** check the cache first; if it's not there, read the database and put a copy in the cache.
- **Analogy:** check the board; if the item isn't on it, check the file and add it to the board.

### TTL (time-to-live)
- **What it is:** how long a cached copy lives before it's thrown away automatically (5 minutes for us).
- **Analogy:** a **"best before" sticker** on the board.

### Cache invalidation
- **What it is:** deleting the cached copy when the real data changes.
- **Analogy:** **wiping the board** the moment the chef says "we're out of burgers".

### CDN (Content Delivery Network)
- **What it is:** a network of servers around the world that keep copies of files that don't change (images, the app's code) close to users.
- **Analogy:** **local corner shops stocking a popular product** so you don't drive to the factory.

### Instances and load balancer
- **What it is:** running **two or more copies** of the app at once. A **load balancer** shares incoming users between them.
- **Analogy:** opening **two tills** with a person directing customers to whichever is free.

### Scaling (horizontal / vertical)
- **Horizontal:** add more copies (more tills). **Vertical:** make one copy bigger (a faster till).

### Search engine (Elasticsearch) *(not used)*
- **What it is:** a specialised system for fast, fuzzy searching over huge amounts of data.
- **Why not:** 50 restaurants fit easily in a normal database search.

---

## H. Shipping changes safely

### Deploy / release
- **Deploy:** putting new code onto the servers. **Release:** letting users actually see the new feature. (Feature flags let these be different moments.)

### CI pipeline (e.g. GitHub Actions)
- **What it is:** an automatic process that runs all tests every time someone proposes a change, and deploys if they pass.
- **Analogy:** a **factory quality-control line**. Every product is checked automatically before it leaves.

### Rolling deploy *(our choice in Q2)*
- **What it is:** updating the running copies **one at a time**, each checked healthy before the next.
- **Analogy:** **changing a car's tyres one at a time while it's still parked safely**. Or, closer to it: renovating a hotel floor by floor while guests stay on the other floors.

### Health check
- **What it is:** a simple "are you OK?" endpoint the platform checks before sending users to a copy.
- **Analogy:** a doctor checking a pulse before letting someone back on the field.

### Blue-green deploy *(alternative)*
- **What it is:** run a complete second copy (green) next to the live one (blue), then switch all traffic at once.
- **Analogy:** setting up a **whole second stage** behind the curtain and swapping instantly.

### Expand / contract migrations
- **What it is:** changing the database in safe steps: **add** the new thing first, move over, and **remove** the old thing only later.
- **Analogy:** **building a new bridge next to the old one**, moving traffic over, *then* demolishing the old bridge.

### Feature flags
- **What it is:** an on/off switch in the code for a feature, so it can be turned on for one restaurant, then everyone.
- **Analogy:** a **light switch for a feature**: installed in the wall already, but off until you're ready.

### CODEOWNERS
- **What it is:** a file listing who must review changes to each part of the code.
- **Analogy:** each room in the building has a **named caretaker** who must approve renovations.

### PaaS (Platform as a Service)
- **What it is:** a hosting company that runs our app and database for us (Render, Heroku, Azure App Service, etc.).
- **Analogy:** **renting a fully serviced office** instead of building and maintaining your own.
- **Why:** we have no DevOps (operations) team.

### DevOps
- **What it is:** the people and practices that keep systems running, deployed and monitored.
- **Analogy:** the building's **maintenance and facilities team**.

### Kubernetes *(not used)*
- **What it is:** a powerful system for running many containers across many servers.
- **Analogy:** a **port's container-crane system**. Amazing at huge scale, overkill for one small shop.

---

## I. Knowing what happened

### Observability
- **What it is:** being able to see what the system is doing and what happened after the fact.
- **Analogy:** **CCTV and a flight recorder** for software.

### Structured logs
- **What it is:** log messages written in a consistent, searchable format.
- **Analogy:** a **neatly filled-in form** instead of scribbled notes.

### Correlation ID
- **What it is:** one ID (we use the order number) stamped on every log line about the same order.
- **Analogy:** a **parcel tracking number** that lets you follow one parcel through every depot.

### Alerts
- **What it is:** automatic warnings when something looks wrong (e.g. "the circuit breaker is open").
- **Analogy:** the **warning lights on a car dashboard**.

### Audit log / append-only
- **What it is:** a record you only ever **add** to, never edit or delete.
- **Analogy:** a **ship's logbook written in pen**.

### Event sourcing *(not used)*
- **What it is:** storing every change as an event and rebuilding the current state by replaying them.
- **Analogy:** knowing your bank balance only by **adding up every transaction since you opened the account**. Powerful, but heavy.

---

## J. Teams and growth

### Conway's law
- **What it is:** systems end up shaped like the teams that build them. We use this on purpose: each team owns a set of modules.
- **Analogy:** if two departments never talk, their parts of the building won't connect well either.

### Contract tests
- **What it is:** automatic checks that one part still provides exactly what another part expects.
- **Analogy:** a **plug-and-socket test**: before shipping a new plug, check it still fits the socket.

### City as data
- **What it is:** adding a new city is just adding a row in the database, not writing new code.
- **Analogy:** adding a new branch to a **franchise list** rather than designing a new restaurant.

---

## K. The acquisition (final challenge)

### Integration
- **What it is:** making two separate systems work together.

### Anti-Corruption Layer (ACL)
- **What it is:** one dedicated translator module between our system and the acquired company's system, so their way of doing things never leaks into ours.
- **Analogy:** a **customs office at a border**. Everything crossing is checked and converted to local rules and currency, and nothing slips through unchecked.

### Model translation
- **What it is:** converting their data shapes and words into ours ("DISPATCHED" means our "OUT_FOR_DELIVERY").
- **Analogy:** **currency exchange**: rands to dollars at a fixed, known rate.

### ID mapping
- **What it is:** a table linking their ID numbers to ours.
- **Analogy:** a **phonebook** saying "their store 8812 = our restaurant 4f2a…".

### Data ownership / source of truth
- **What it is:** for every piece of information, exactly one system is the official owner; everyone else holds labelled copies.
- **Analogy:** the **original birth certificate** vs certified copies.

### Synced copy (catalogue sync)
- **What it is:** regularly copying their restaurants and menus into our system, read-only.
- **Analogy:** a **printed brochure** of another shop's menu, refreshed often. You read the brochure instead of phoning them every time.

### API versioning
- **What it is:** labelling versions of an interface (v1, v2) so changes don't break existing users.
- **Analogy:** **phone charger generations**. Old cables still work with old phones.

### Consumer-driven contract tests (Pact)
- **What it is:** we write down exactly what we need from their system, and their build checks it automatically.
- **Analogy:** giving a supplier your **exact spec sheet**, which they check every batch against.

### Tolerant reader
- **What it is:** read only the fields you need and ignore anything extra.
- **Analogy:** reading a long letter and only paying attention to the bits addressed to you.

### Strangler fig *(future option)*
- **What it is:** gradually replacing an old system piece by piece behind a stable front, until the old one can be removed.
- **Analogy:** a **strangler fig tree** grows around an old tree until it can stand on its own. Or renovating a house one room at a time while living in it.

### ESB / iPaaS *(not used)*
- **What it is:** big commercial "integration platforms" that sit between many systems.
- **Analogy:** hiring an **expensive outside agency** to pass every message between two companies. Overkill for two systems.

---

# Part 3: The one-sentence version of each question

| Question | In plain English |
|----------|------------------|
| **Q1** | Build **one tidy building with locked rooms** (modular monolith), organise each room **by job, like meal kits** (vertical slices), and keep outside companies behind **travel adapters**. |
| **Q2** | Don't knock the building down. Add a **specials board** (cache), a **back kitchen** (worker + queue) so customers don't wait, a **photocopy room** for accountants (read replica), and **renovate floor by floor** (rolling deploys). |
| **Q3** | When the card machine goes quiet, write **"don't know yet"**, not "failed". Give every order a **ticket number** so double-clicks don't double-charge, use a **circuit breaker**, and **check the bank statement** to fix anything uncertain. |
| **Challenge** | Put a **customs office** (anti-corruption layer) at the border with the company we bought. Each side keeps its own records, and everything crossing is translated. |
