> **Superseded:** the final, reworked answers (modular monolith + vertical slices) are in [`FinalAnswers/`](FinalAnswers/README.md). This file is kept as history. See `FinalAnswers/CHANGES-AND-REASONS.md` for what changed and why.

# Question 1: Architecture Design - Answers

## Context Constraints

| Constraint | Value |
|------------|-------|
| Restaurants | 1 |
| Drivers | Small number (employed by restaurant) |
| Customers | Small initial base |
| Developers | 3 |
| Budget | Very limited |
| DevOps | None |
| Goal | Prove the business model |

---

## 1. Components

### 1.1 What Components Exist?

| # | Component | Type | Purpose |
|---|-----------|------|---------|
| 1 | **Identity/Auth** | Core | Authentication for all actors (Customer, Restaurant, Driver) |
| 2 | **Customer** | Core | Customer profiles and delivery addresses |
| 3 | **Restaurant** | Core | Restaurant profiles, settings, and operational status |
| 4 | **Menu** | Core | Menu items, pricing, and availability management |
| 5 | **Order** | Core | Order lifecycle, state management, and history |
| 6 | **Payment** | Core | Payment processing via external provider |
| 7 | **Delivery** | Core | Driver management, shifts, and delivery tracking |
| 8 | **Reporting** | Supporting | Business metrics and aggregated data |
| - | **Notification** | Utility | Push notifications (used by other components) |

### 1.2 Component Responsibilities

#### Identity/Auth
- User registration (Customer self-registers; Restaurant registered by platform)
- Driver account creation (by Restaurant)
- Login/logout for all actor types
- Session management
- User type identification and routing

#### Customer
- Manage customer profile information
- Store multiple delivery addresses
- Link to order history (reads from Order component)

#### Restaurant
- Manage restaurant profile
- Set operational status (open/closed)
- Configure delivery fee
- View order queues (incoming and preparation)
- Onboard drivers

#### Menu
- Add, edit, remove menu items
- Set item prices
- Toggle item availability (e.g., item runs out)

#### Order
- Create orders from customer basket
- Calculate order totals (items + delivery fee + platform fee)
- Manage order state transitions
- Store order history
- Provide queue views to restaurant

#### Payment
- Process payments via payment provider (Stripe)
- Track payment state (pending, processing, succeeded, failed)
- Abstract payment provider details from rest of system

#### Delivery
- Manage driver profiles
- Track driver shift status (signed in/out)
- Handle delivery assignments by restaurant
- Track delivery status progression
- Store delivery history

#### Reporting
- Aggregate data from other components
- Generate platform-wide metrics (total orders, revenue)
- Generate restaurant-specific metrics (their orders, completion rates)

#### Notification (Utility)
- Send push notifications for order events
- Not a bounded context - used by other components

---

## 2. Data Models

### 2.1 Entities

| Entity | Owned By | Key Fields |
|--------|----------|------------|
| **User** | Identity/Auth | user_id, email, password_hash, user_type |
| **CustomerProfile** | Customer | customer_id, name, phone, default_address_id |
| **CustomerAddress** | Customer | address_id, customer_id, address_line, city, postcode, is_default |
| **RestaurantProfile** | Restaurant | restaurant_id, name, address, phone, delivery_fee, is_open |
| **MenuItem** | Menu | item_id, restaurant_id, name, description, price, is_available |
| **DriverProfile** | Delivery | driver_id, restaurant_id, name, phone |
| **DriverShift** | Delivery | shift_id, driver_id, signed_in_at, signed_out_at, is_active |
| **Order** | Order | order_id, customer_id, restaurant_id, driver_id, state, totals |
| **OrderItem** | Order | order_item_id, order_id, item_name (snapshot), item_price (snapshot), quantity |
| **OrderStateHistory** | Order | history_id, order_id, from_state, to_state, changed_at, changed_by |
| **Payment** | Payment | payment_id, order_id, amount, currency, provider_reference, state |
| **Delivery** | Delivery | delivery_id, order_id, driver_id, state, timestamps |

### 2.2 Key Relationships

| From | To | Relationship |
|------|----|--------------|
| User | CustomerProfile | 1:1 (if user_type = Customer) |
| User | RestaurantProfile | 1:1 (if user_type = Restaurant) |
| User | DriverProfile | 1:1 (if user_type = Driver) |
| Restaurant | MenuItem | 1:many |
| Restaurant | DriverProfile | 1:many (drivers belong to restaurant) |
| Customer | Order | 1:many |
| Restaurant | Order | 1:many |
| Order | OrderItem | 1:many |
| Order | Payment | 1:1 |
| Order | Delivery | 1:1 |
| Driver | Delivery | 1:many |

---

## 3. Boundaries

### 3.1 Deployment Topology

**Choice: Modular Monolith**

A single deployable application with clear internal module boundaries and a shared database.

**Why Modular Monolith?**

| Factor | Why It Fits |
|--------|-------------|
| 3 developers | Cannot manage multiple services |
| No DevOps | Simple deployment (single artifact) |
| Limited budget | One app, one database |
| Prove business model | Fast to develop and iterate |

**Why NOT Microservices?**

- Requires infrastructure we don't have (service discovery, containers, orchestration)
- 3 developers cannot manage network complexity
- No DevOps team to handle deployments
- Overkill for 1 restaurant

### 3.2 Internal Architecture

**Choice: Pragmatic Layered Architecture with Domain-Driven Design**

Each module follows:
```
module/
├── presentation/      # Controllers, API endpoints
├── application/       # Use cases, orchestration
├── domain/            # Business logic (entities, value objects)
└── infrastructure/    # Database, external services
```

**Why This Pattern?**

| Factor | Why It Fits |
|--------|-------------|
| Team familiarity | Layered architecture is widely understood |
| Testability | Clear separation allows unit testing |
| Evolvability | Can extract modules later if needed |
| Pragmatic | Not full Clean Architecture ceremony |

### 3.3 Module Boundaries

**Enforcement:**
- Each module in separate folder
- Each module owns its database tables
- Modules communicate through interfaces
- Cross-module reads allowed; writes only to own tables

**Boundary Rules:**

| Rule | Description |
|------|-------------|
| Table ownership | Only Order module writes to orders table |
| Interface calls | Modules call each other through defined interfaces |
| No direct DB access | Module A doesn't query Module B's tables directly (uses interface) |
| Folder separation | Physical separation in codebase |

---

## 4. Communication Patterns

### 4.1 Inter-Module Communication

**Choice: Direct In-Process Calls**

```
Order Module ──calls──> Payment Module ──calls──> Stripe
```

Modules call each other directly through interfaces within the same process.

**Why Direct Calls?**

| Factor | Why It Fits |
|--------|-------------|
| Simplicity | No message broker needed |
| Debugging | Stack trace shows full flow |
| Transactional | Can use database transactions |
| Speed | No network latency |

**Why NOT Event-Driven?**

- More infrastructure (message broker)
- Harder to debug
- Eventually consistent
- Overkill for 3 developers and MVP

### 4.2 External Communication

**Payment Provider:**
- Wrapped in Payment module
- Interface abstracts provider details
- Easy to swap providers (implement new adapter)

```
Order Module ──> PaymentService (interface) ──> StripeAdapter ──> Stripe API
```

---

## 5. Order Flow and States

### 5.1 Order States

| State | Description | Transitions To |
|-------|-------------|----------------|
| `PENDING_ACCEPTANCE` | Awaiting restaurant decision | ACCEPTED, REJECTED |
| `REJECTED` | Restaurant rejected | (terminal) |
| `ACCEPTED` | Restaurant accepted, payment pending | PREPARING, PAYMENT_FAILED |
| `PAYMENT_FAILED` | Payment could not be processed | (terminal) |
| `PREPARING` | Being prepared | READY |
| `READY` | Food ready for pickup | OUT_FOR_DELIVERY |
| `OUT_FOR_DELIVERY` | Driver en route | DELIVERED |
| `DELIVERED` | Driver completed delivery | COMPLETED |
| `COMPLETED` | Order finished | (terminal) |

### 5.2 Payment Timing

**Decision: Payment AFTER Restaurant Accepts**

**Why?**
- Avoids refund complexity if restaurant rejects
- Customer only charged for fulfillable orders
- Simpler payment state management

### 5.3 Order Total Calculation

```
items_total   = SUM(item_price × quantity)
delivery_fee  = restaurant.delivery_fee
subtotal      = items_total + delivery_fee
platform_fee  = subtotal × 10%
total_amount  = subtotal + platform_fee
```

---

## 6. Driver Model

### 6.1 Decision: Drivers Owned by Restaurants

Drivers belong to restaurants (like pizza delivery), not a gig-economy pool.

**Why?**
- Matches the actual business model described
- No driver matching algorithm needed
- Restaurant manages their own staff
- Platform provides tools, not workforce

**Implications:**
- Driver account created by restaurant
- Driver signs in/out of shifts
- Restaurant assigns driver to ready orders
- Driver acknowledges (not accept/reject - they're employed)

### 6.2 Delivery States

| State | Description |
|-------|-------------|
| `ASSIGNED` | Restaurant assigned driver |
| `ACKNOWLEDGED` | Driver confirmed they see it |
| `PICKED_UP` | Driver has the food |
| `IN_ROUTE` | Driver heading to customer |
| `DELIVERED` | Driver completed delivery |

---

## 7. Authentication

### 7.1 User Types

| Type ID | Actor | Registration |
|---------|-------|--------------|
| 1 | Restaurant | Platform onboards |
| 2 | Customer | Self-registration |
| 3 | Driver | Restaurant onboards |

### 7.2 Flow

1. User logs in via Identity/Auth module
2. Module validates credentials
3. Returns user with type marker
4. Client routes to appropriate experience (Restaurant Dashboard, Customer App, Driver App)

---

## 8. Architectural Decisions

### Decision 1: Modular Monolith with Shared Database

**What:** Single deployable application, clear module boundaries, shared PostgreSQL database.

**Why:** 3 developers, no DevOps, limited budget, need to prove business model fast.

**Trade-offs:** Risk of coupling if not disciplined; cannot scale components independently.

### Decision 2: Payment After Restaurant Acceptance

**What:** Payment processed only after restaurant accepts order.

**Why:** Avoids refunds on rejected orders; simpler state management.

**Trade-offs:** Small window where restaurant accepts but payment fails (edge case).

### Decision 3: Drivers Owned by Restaurants

**What:** Drivers belong to restaurants, not platform gig pool.

**Why:** Matches business model; no matching algorithm needed; simpler system.

**Trade-offs:** Cannot optimize drivers across restaurants.

### Decision 4: Pragmatic Layered + DDD

**What:** Each module uses layered architecture with domain entities.

**Why:** Team familiar with pattern; testable; evolvable.

**Trade-offs:** Some ceremony; need discipline to keep boundaries clean.

---

## 9. Risks and Mitigations

### Risk 1: Module Coupling Over Time

**Risk:** Modules become tangled through shared data or unclear boundaries.

**Mitigation:**
- Enforce table ownership
- Use interfaces for cross-module calls
- Code review for boundary violations
- Physical folder separation

### Risk 2: Payment Provider Dependency

**Risk:** Single external dependency that can fail.

**Mitigation:**
- Wrapped in Payment module (can swap)
- Simple failure handling for MVP
- Manual intervention possible at small scale

### Risk 3: Single Database Performance

**Risk:** As data grows, single database may become bottleneck.

**Mitigation:**
- Acceptable for MVP scale (1 restaurant)
- Index best practices
- Monitor query performance
- Will address in Q2 if needed

---

## 10. Deliberate Omissions

### Not Done: Real-Time GPS Tracking

**Why not:** Significant complexity; not required for proving business model; status updates sufficient.

**When to reconsider:** If customers demand it or delivery disputes increase.

### Not Done: Customer Ratings/Reviews

**Why not:** 1 restaurant doesn't need competitive differentiation; adds entities and UI flows.

**When to reconsider:** When multiple restaurants compete (Q2+).

---

## Summary

| Aspect | Decision |
|--------|----------|
| Deployment | Modular Monolith |
| Internal Architecture | Pragmatic Layered + DDD |
| Database | Single PostgreSQL, shared |
| Communication | Direct in-process calls |
| Components | 8 core modules + 1 utility |
| Payment Timing | After restaurant accepts |
| Driver Model | Owned by restaurants |
| External Integration | Wrapped in interfaces |

**Guiding Principle:** Design for current constraints (3 devs, 1 restaurant, no DevOps, limited budget). Don't solve problems we haven't been given.
