# Food Delivery Platform - Technical Documentation

This document contains the complete technical specification for the food delivery platform architecture, including components, data models, relationships, communication patterns, and architectural decisions.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Components](#2-components)
3. [Data Models](#3-data-models)
4. [Entity Relationship Diagram](#4-entity-relationship-diagram)
5. [Component Communication](#5-component-communication)
6. [External Systems](#6-external-systems)
7. [Authentication](#7-authentication)
8. [Business Requirements Fulfillment](#8-business-requirements-fulfillment)
9. [Architectural Decisions](#9-architectural-decisions)
10. [Risks](#10-risks)
11. [Deliberate Omissions](#11-deliberate-omissions)

---

## 1. System Overview

### 1.1 Constraints (Question 1 Scope)

| Constraint | Value |
|------------|-------|
| Restaurants | 1 |
| Drivers | Small number |
| Customer base | Small initial |
| Developers | 3 |
| Infrastructure budget | Very limited |
| DevOps team | None |
| Primary goal | Prove the business model |

### 1.2 Design Principles

1. **Don't solve problems we haven't been given** - No speculative complexity
2. **Complexity must justify the problem it solves** - Every addition needs a reason
3. **Design for current scope, not hypothetical future** - MVP first

### 1.3 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SINGLE APPLICATION                           │
│                     (Modular Monolith)                              │
│                                                                     │
│   ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐          │
│   │ Identity │  │ Customer │  │Restaurant│  │   Menu   │          │
│   │   Auth   │  │          │  │          │  │          │          │
│   └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘          │
│        │             │             │             │                 │
│   ┌────┴─────┐  ┌────┴─────┐  ┌────┴─────┐  ┌────┴─────┐          │
│   │  Order   │  │ Payment  │  │ Delivery │  │Reporting │          │
│   │          │  │          │  │          │  │          │          │
│   └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘          │
│        │             │             │             │                 │
│        │      Direct calls between modules       │                 │
│        │      through defined interfaces         │                 │
│        │             │             │             │                 │
│        ▼             ▼             ▼             ▼                 │
│   ┌─────────────────────────────────────────────────────────────┐ │
│   │                     SINGLE DATABASE                          │ │
│   └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. Components

### 2.1 Component Overview

| # | Component | Type | Description |
|---|-----------|------|-------------|
| 1 | **Identity/Auth** | Core | Centralized authentication for all actors |
| 2 | **Customer** | Core | Customer profiles and addresses |
| 3 | **Restaurant** | Core | Restaurant profiles and settings |
| 4 | **Menu** | Core | Menu items, pricing, availability |
| 5 | **Order** | Core | Order lifecycle and state management |
| 6 | **Payment** | Core | Payment processing and records |
| 7 | **Delivery** | Core | Driver management and delivery tracking |
| 8 | **Reporting** | Supporting | Aggregated metrics and reports |
| - | **Notification** | Utility | Push notifications (used by other components) |

### 2.2 Component Details

#### 2.2.1 Identity/Auth Component

**Responsibilities:**
- User registration (Customer self-registers, Restaurant registered by platform)
- Driver account creation (by Restaurant)
- Login/logout for all actor types
- Session management
- User type identification

**Data Owned:**
- User accounts
- Credentials
- Sessions
- User type markers

**User Types:**

| Type ID | Actor | Registration |
|---------|-------|--------------|
| 1 | Restaurant | Platform onboards |
| 2 | Customer | Self-registration |
| 3 | Driver | Restaurant onboards |

**Notes:**
- Driver ID links to restaurant (driver belongs to restaurant)
- Single login system, user type determines experience/routing

---

#### 2.2.2 Customer Component

**Responsibilities:**
- Manage customer profile
- Store delivery addresses
- Link to order history (read from Order component)

**Data Owned:**
- Customer profiles
- Customer addresses

**Notes:**
- Addresses include a default flag for convenience
- Multiple addresses supported per customer

---

#### 2.2.3 Restaurant Component

**Responsibilities:**
- Manage restaurant profile
- Restaurant settings (delivery fee, open/closed status)
- View order queues (reads from Order component)
- Onboard drivers (creates via Identity/Auth)

**Data Owned:**
- Restaurant profiles
- Restaurant settings

**Notes:**
- Queue views are read models from Order component filtered by state
- Incoming queue: orders in state `PENDING_ACCEPTANCE` for this restaurant
- Preparation queue: orders in state `PREPARING` for this restaurant

---

#### 2.2.4 Menu Component

**Responsibilities:**
- Manage menu items
- Set item prices
- Toggle item availability (available/unavailable)

**Data Owned:**
- Menu items
- Item availability states

**Notes:**
- Menu belongs to Restaurant (1:many relationship)
- Customer sees only available items (or unavailable shown as greyed out)
- Restaurant can toggle availability anytime (e.g., ice cream machine broken)

---

#### 2.2.5 Order Component

**Responsibilities:**
- Create orders from basket
- Manage order state transitions
- Calculate order totals
- Store order history
- Provide queue views to Restaurant

**Data Owned:**
- Orders
- Order items
- Order state history

**Order States:**

| State | Description | Next States |
|-------|-------------|-------------|
| `PENDING_ACCEPTANCE` | Awaiting restaurant decision | `ACCEPTED`, `REJECTED` |
| `REJECTED` | Restaurant rejected order | (terminal) |
| `ACCEPTED` | Restaurant accepted, payment pending | `PREPARING`, `PAYMENT_FAILED` |
| `PAYMENT_FAILED` | Payment could not be processed | (terminal) |
| `PREPARING` | Being prepared by restaurant | `READY` |
| `READY` | Food ready for pickup | `OUT_FOR_DELIVERY` |
| `OUT_FOR_DELIVERY` | Driver en route to customer | `DELIVERED` |
| `DELIVERED` | Driver marked as delivered | `COMPLETED` |
| `COMPLETED` | Order finished | (terminal) |

**Order Total Calculation:**
```
items_total   = SUM(item_price × quantity)    [from menu]
delivery_fee  = restaurant.delivery_fee       [restaurant sets]
subtotal      = items_total + delivery_fee
platform_fee  = subtotal × PLATFORM_PERCENTAGE [platform sets, e.g., 10%]
total_amount  = subtotal + platform_fee       [customer pays]
```

---

#### 2.2.6 Payment Component

**Responsibilities:**
- Process payments via payment provider
- Track payment state
- Store payment records
- Abstract payment provider details

**Data Owned:**
- Payment records
- Payment states

**Payment States:**

| State | Description |
|-------|-------------|
| `PENDING` | Payment initiated |
| `PROCESSING` | Sent to provider |
| `SUCCEEDED` | Provider confirmed success |
| `FAILED` | Provider returned failure |

**External Integration:**
- Wraps payment provider (Stripe, etc.)
- Rest of system doesn't know which provider
- Easy to swap providers later

---

#### 2.2.7 Delivery Component

**Responsibilities:**
- Manage driver profiles
- Track driver shift status (signed in/out)
- Handle delivery assignments
- Track delivery status
- Store delivery history

**Data Owned:**
- Driver profiles
- Driver shifts
- Delivery records

**Driver Shift States:**

| State | Description |
|-------|-------------|
| `SIGNED_OUT` | Not working |
| `SIGNED_IN` | Available for assignments |

**Delivery States:**

| State | Description |
|-------|-------------|
| `ASSIGNED` | Restaurant assigned driver |
| `ACKNOWLEDGED` | Driver confirmed they see it |
| `PICKED_UP` | Driver has the food |
| `IN_ROUTE` | Driver heading to customer |
| `DELIVERED` | Driver completed delivery |

**Notes:**
- Driver belongs to restaurant (1:many)
- Restaurant selects from their signed-in drivers
- Driver acknowledges (not accept/reject - they're employed)

---

#### 2.2.8 Reporting Component

**Responsibilities:**
- Aggregate data from other components
- Generate metrics for platform (global view)
- Generate metrics for restaurants (their data)

**Data Owned:**
- None (reads from other components)

**Reports:**

| Actor | Metrics |
|-------|---------|
| Platform/Business | Restaurants onboarded, total orders, total revenue (platform fees), basic averages |
| Restaurant | Their orders, delivery completion rates, completion times |

**Notes:**
- Simple for MVP - basic aggregations
- No customer satisfaction ratings for now (future scope)

---

#### 2.2.9 Notification (Utility)

**Responsibilities:**
- Send push notifications
- Used by other components (not a bounded context)

**Usage:**
- Order placed → notify restaurant
- Delivery assigned → notify driver
- Status changes → notify customer

**Notes:**
- Utility service, not standalone component
- For MVP: push notifications only
- Email/SMS could be added later (Question 2+)

---

## 3. Data Models

### 3.1 User

| Field | Type | Description |
|-------|------|-------------|
| `user_id` | UUID | Primary key |
| `email` | String | Login identifier |
| `password_hash` | String | Hashed password |
| `user_type` | Integer | 1=Restaurant, 2=Customer, 3=Driver |
| `created_at` | Timestamp | Account creation time |

### 3.2 CustomerProfile

| Field | Type | Description |
|-------|------|-------------|
| `customer_id` | UUID | PK, FK to User |
| `name` | String | Customer's name |
| `phone` | String | Contact number |
| `default_address_id` | UUID | FK to CustomerAddress |

### 3.3 CustomerAddress

| Field | Type | Description |
|-------|------|-------------|
| `address_id` | UUID | Primary key |
| `customer_id` | UUID | FK to CustomerProfile |
| `address_line` | String | Street address |
| `city` | String | City name |
| `postcode` | String | Postal code |
| `is_default` | Boolean | Default address flag |

### 3.4 RestaurantProfile

| Field | Type | Description |
|-------|------|-------------|
| `restaurant_id` | UUID | PK, FK to User |
| `name` | String | Restaurant name |
| `address` | String | Restaurant location |
| `phone` | String | Contact number |
| `delivery_fee` | Decimal | Fee charged for delivery |
| `is_open` | Boolean | Currently accepting orders |

### 3.5 MenuItem

| Field | Type | Description |
|-------|------|-------------|
| `item_id` | UUID | Primary key |
| `restaurant_id` | UUID | FK to RestaurantProfile |
| `name` | String | Item name |
| `description` | String | Item description |
| `price` | Decimal | Item price |
| `is_available` | Boolean | Currently available |
| `created_at` | Timestamp | Creation time |

### 3.6 DriverProfile

| Field | Type | Description |
|-------|------|-------------|
| `driver_id` | UUID | PK, FK to User |
| `restaurant_id` | UUID | FK to RestaurantProfile |
| `name` | String | Driver's name |
| `phone` | String | Contact number |

### 3.7 DriverShift

| Field | Type | Description |
|-------|------|-------------|
| `shift_id` | UUID | Primary key |
| `driver_id` | UUID | FK to DriverProfile |
| `signed_in_at` | Timestamp | Shift start time |
| `signed_out_at` | Timestamp | Shift end time (nullable) |
| `is_active` | Boolean | Currently signed in |

### 3.8 Order

| Field | Type | Description |
|-------|------|-------------|
| `order_id` | UUID | Primary key |
| `customer_id` | UUID | FK to CustomerProfile |
| `restaurant_id` | UUID | FK to RestaurantProfile |
| `driver_id` | UUID | FK to DriverProfile (nullable) |
| `state` | String | Current order state |
| `delivery_address` | String | Delivery location (snapshot) |
| `items_total` | Decimal | Sum of item prices |
| `delivery_fee` | Decimal | Delivery fee (snapshot) |
| `platform_fee` | Decimal | Platform surcharge |
| `total_amount` | Decimal | Final amount customer pays |
| `created_at` | Timestamp | Order creation time |
| `updated_at` | Timestamp | Last update time |

### 3.9 OrderItem

| Field | Type | Description |
|-------|------|-------------|
| `order_item_id` | UUID | Primary key |
| `order_id` | UUID | FK to Order |
| `menu_item_id` | UUID | FK to MenuItem |
| `item_name` | String | Item name (snapshot) |
| `item_price` | Decimal | Item price (snapshot) |
| `quantity` | Integer | Number of items |

### 3.10 OrderStateHistory

| Field | Type | Description |
|-------|------|-------------|
| `history_id` | UUID | Primary key |
| `order_id` | UUID | FK to Order |
| `from_state` | String | Previous state |
| `to_state` | String | New state |
| `changed_at` | Timestamp | When state changed |
| `changed_by` | UUID | User who made the change |

### 3.11 Payment

| Field | Type | Description |
|-------|------|-------------|
| `payment_id` | UUID | Primary key |
| `order_id` | UUID | FK to Order |
| `amount` | Decimal | Payment amount |
| `currency` | String | Currency code (e.g., USD) |
| `provider_reference` | String | External provider ID |
| `state` | String | Payment state |
| `created_at` | Timestamp | Creation time |
| `processed_at` | Timestamp | Processing time |

### 3.12 Delivery

| Field | Type | Description |
|-------|------|-------------|
| `delivery_id` | UUID | Primary key |
| `order_id` | UUID | FK to Order |
| `driver_id` | UUID | FK to DriverProfile |
| `state` | String | Delivery state |
| `assigned_at` | Timestamp | Assignment time |
| `acknowledged_at` | Timestamp | Acknowledgement time |
| `picked_up_at` | Timestamp | Pickup time |
| `delivered_at` | Timestamp | Delivery completion time |

---

## 4. Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                            ENTITY RELATIONSHIP DIAGRAM                          │
└─────────────────────────────────────────────────────────────────────────────────┘

    ┌─────────────────┐
    │      USER       │
    ├─────────────────┤
    │ PK user_id      │
    │    email        │
    │    password_hash│
    │    user_type    │──────┐
    │    created_at   │      │ user_type determines
    └────────┬────────┘      │ which profile table
             │               │
    ┌────────┼────────┬──────┘
    │        │        │
    │        │        │
    ▼        ▼        ▼
┌─────────┐ ┌──────────────┐ ┌─────────────┐
│CUSTOMER │ │  RESTAURANT  │ │   DRIVER    │
│ PROFILE │ │   PROFILE    │ │  PROFILE    │
├─────────┤ ├──────────────┤ ├─────────────┤
│PK,FK    │ │PK,FK         │ │PK,FK        │
│customer_│ │restaurant_id │ │driver_id    │
│  id     │ │name          │ │FK           │
│name     │ │address       │ │restaurant_id│◀──┐
│phone    │ │phone         │ │name         │   │
│         │ │delivery_fee  │ │phone        │   │
└────┬────┘ │is_open       │ └──────┬──────┘   │
     │      └──────┬───────┘        │          │
     │             │                │          │
     │             │    ┌───────────┘          │
     │             │    │                      │
     │             │    │  belongs to          │
     │             │    └──────────────────────┘
     │             │
     │             │ has many
     │             ▼
     │      ┌──────────────┐
     │      │  MENU_ITEM   │
     │      ├──────────────┤
     │      │PK item_id    │
     │      │FK            │
     │      │restaurant_id │
     │      │name          │
     │      │description   │
     │      │price         │
     │      │is_available  │
     │      └──────────────┘
     │
     │ places many
     │             receives many
     │      ┌─────────────────┐
     │      │                 │
     ▼      ▼                 │
┌──────────────────┐          │
│      ORDER       │          │
├──────────────────┤          │
│PK order_id       │          │
│FK customer_id    │──────────┘
│FK restaurant_id  │
│FK driver_id      │ (nullable)
│state             │
│delivery_address  │
│items_total       │
│delivery_fee      │
│platform_fee      │
│total_amount      │
│created_at        │
│updated_at        │
└────────┬─────────┘
         │
         │ has many
         ▼
┌──────────────────┐
│   ORDER_ITEM     │
├──────────────────┤
│PK order_item_id  │
│FK order_id       │
│FK menu_item_id   │
│item_name         │ (snapshot)
│item_price        │ (snapshot)
│quantity          │
└──────────────────┘
         │
         │
         │
┌────────┴─────────┐
│                  │
▼                  ▼
┌──────────────┐  ┌──────────────┐
│   PAYMENT    │  │   DELIVERY   │
├──────────────┤  ├──────────────┤
│PK payment_id │  │PK delivery_id│
│FK order_id   │  │FK order_id   │
│amount        │  │FK driver_id  │
│currency      │  │state         │
│provider_ref  │  │assigned_at   │
│state         │  │acknowledged_ │
│created_at    │  │  at          │
│processed_at  │  │picked_up_at  │
└──────────────┘  │delivered_at  │
                  └──────────────┘


┌──────────────────┐
│  DRIVER_SHIFT    │
├──────────────────┤
│PK shift_id       │
│FK driver_id      │
│signed_in_at      │
│signed_out_at     │
│is_active         │
└──────────────────┘


┌────────────────────┐
│ ORDER_STATE_HISTORY│
├────────────────────┤
│PK history_id       │
│FK order_id         │
│from_state          │
│to_state            │
│changed_at          │
│changed_by          │
└────────────────────┘
```

### 4.1 Relationship Summary

| From | To | Relationship | Description |
|------|----|--------------|-------------|
| User | CustomerProfile | 1:1 | User of type Customer has profile |
| User | RestaurantProfile | 1:1 | User of type Restaurant has profile |
| User | DriverProfile | 1:1 | User of type Driver has profile |
| Restaurant | MenuItem | 1:many | Restaurant has menu items |
| Restaurant | DriverProfile | 1:many | Restaurant employs drivers |
| Customer | Order | 1:many | Customer places orders |
| Restaurant | Order | 1:many | Restaurant receives orders |
| Order | OrderItem | 1:many | Order contains items |
| Order | Payment | 1:1 | Order has one payment |
| Order | Delivery | 1:1 | Order has one delivery |
| Driver | Delivery | 1:many | Driver performs deliveries |
| Driver | DriverShift | 1:many | Driver has shift history |
| Order | OrderStateHistory | 1:many | Order has state change log |

---

## 5. Component Communication

### 5.1 Chosen Pattern: Direct In-Process Calls + Shared Database

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SINGLE APPLICATION                           │
│                                                                     │
│   ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐          │
│   │ Identity │  │ Customer │  │Restaurant│  │   Menu   │          │
│   │   Auth   │  │          │  │          │  │          │          │
│   └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘          │
│        │             │             │             │                 │
│   ┌────┴─────┐  ┌────┴─────┐  ┌────┴─────┐  ┌────┴─────┐          │
│   │  Order   │  │ Payment  │  │ Delivery │  │Reporting │          │
│   │          │  │          │  │          │  │          │          │
│   └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘          │
│        │             │             │             │                 │
│        │      Direct calls between modules       │                 │
│        │      through defined interfaces         │                 │
│        │             │             │             │                 │
│        ▼             ▼             ▼             ▼                 │
│   ┌─────────────────────────────────────────────────────────────┐ │
│   │                     SINGLE DATABASE                          │ │
│   │                                                              │ │
│   │  Each module owns its tables                                │ │
│   │  Modules can READ each other's data                         │ │
│   │  Only WRITE to own tables                                   │ │
│   │                                                              │ │
│   └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.2 Why This Pattern?

| Criteria | Fit |
|----------|-----|
| 3 developers | Simple to build and debug |
| Limited budget | One app, one database |
| No DevOps | Simple deployment |
| Prove business model | Fast to develop |

### 5.3 Rules to Keep It Clean

1. **Each module owns its tables** - Only Order module writes to orders table
2. **Defined interfaces** - Modules call each other through interfaces, not random function calls
3. **Cross-module reads allowed** - Restaurant can read orders to show queue
4. **Separate folders** - Keep modules in separate folders with clear boundaries

### 5.4 Example Flow: Customer Places Order

```
1. Customer submits order
        │
        ▼
2. Order Module: Creates order (state: PENDING_ACCEPTANCE)
        │
        ▼
3. Order Module calls ──▶ Restaurant Module: "Is this restaurant open?"
        │                         │
        │◀── Yes ─────────────────┘
        ▼
4. Order Module calls ──▶ Menu Module: "Validate these items exist"
        │                         │
        │◀── Valid ───────────────┘
        ▼
5. Order goes to restaurant (state: PENDING_ACCEPTANCE)
        │
        ▼
6. Restaurant accepts
        │
        ▼
7. Order Module calls ──▶ Payment Module: "Process payment"
        │                         │
        │◀── Success ─────────────┘
        ▼
8. Order confirmed (state: PREPARING)
```

### 5.5 Alternative Pattern (Noted for Question 2/3): Event-Driven/Messaging

```
┌─────────┐    ┌─────────────┐    ┌─────────────┐
│  Order  │───▶│  EVENT BUS  │───▶│ Restaurant  │
└─────────┘    │             │    └─────────────┘
               │ OrderPlaced │
               │ event       │───▶│  Payment    │
               └─────────────┘    └─────────────┘
```

**How it works:**
- Publisher-Subscriber (Observer) pattern
- Component A publishes event, interested components subscribe
- Loose coupling, components don't know about each other

**Why not for Question 1:**
- More infrastructure (message broker needed)
- Harder to debug
- Eventually consistent
- Overkill for 3 developers and MVP

**When to consider (Question 2/3):**
- Notifications slowing down operations
- Need to decouple components
- Scale concerns

---

## 6. External Systems

### 6.1 Payment Provider Integration

**Pattern: Wrapped in Payment Module**

```
┌─────────────────────────────────────────────────────────────────────┐
│                        APPLICATION                                  │
│                                                                     │
│   ┌──────────┐         ┌──────────────────────────┐                │
│   │  Order   │────────▶│     Payment Module       │                │
│   │  Module  │         │                          │                │
│   └──────────┘         │  ┌────────────────────┐  │                │
│                        │  │ PaymentService     │  │                │
│                        │  │ (interface)        │  │                │
│                        │  └─────────┬──────────┘  │                │
│                        │            │             │                │
│                        │  ┌─────────▼──────────┐  │                │
│                        │  │ StripeAdapter      │  │───────────────────▶ Stripe API
│                        │  │ (implementation)   │  │     EXTERNAL
│                        │  └────────────────────┘  │
│                        └──────────────────────────┘                │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Benefits:**
- Order module just says "process payment" - doesn't know about Stripe
- Easy to swap providers (implement new adapter)
- Easy to test (mock PaymentService interface)
- Provider-specific logic contained in adapter

### 6.2 Failure Handling (Simple for Q1)

| Scenario | Response |
|----------|----------|
| Payment succeeds | Order continues to PREPARING |
| Payment fails | Order goes to PAYMENT_FAILED, customer notified |
| Payment times out | Treat as failure, customer can retry |

**Note for Question 3:** Payment provider outage scenario will require more sophisticated handling (idempotency, reconciliation, etc.)

### 6.3 Other External Systems

| System | MVP Status | Notes |
|--------|------------|-------|
| Payment Provider | Required | Core functionality |
| Push Notifications | Required | Firebase, etc. (wrapped in Notification utility) |
| Email/SMS | Not for MVP | Could add in Q2 if needed |
| Maps/Geocoding | Not for MVP | No live tracking in Q1 |
| Analytics | Not for MVP | Basic reporting is internal |

---

## 7. Authentication

### 7.1 Authentication Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                     AUTHENTICATION FLOW                          │
└──────────────────────────────────────────────────────────────────┘

                    ┌─────────────────┐
                    │   User Login    │
                    │   (any actor)   │
                    └────────┬────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │  Identity/Auth  │
                    │    Module       │
                    └────────┬────────┘
                             │
                             │ Validate credentials
                             │ Return user + type
                             │
                             ▼
                    ┌─────────────────┐
                    │  User Type?     │
                    └────────┬────────┘
                             │
          ┌──────────────────┼──────────────────┐
          │                  │                  │
          ▼                  ▼                  ▼
   ┌─────────────┐   ┌─────────────┐   ┌─────────────┐
   │ Type = 1    │   │ Type = 2    │   │ Type = 3    │
   │ RESTAURANT  │   │ CUSTOMER    │   │ DRIVER      │
   └──────┬──────┘   └──────┬──────┘   └──────┬──────┘
          │                  │                  │
          ▼                  ▼                  ▼
   ┌─────────────┐   ┌─────────────┐   ┌─────────────┐
   │ Restaurant  │   │ Customer    │   │ Driver      │
   │ Dashboard   │   │ App         │   │ App         │
   └─────────────┘   └─────────────┘   └─────────────┘
```

### 7.2 User Type Markers

| Type ID | Actor | Registration Method |
|---------|-------|---------------------|
| 1 | Restaurant | Platform onboards |
| 2 | Customer | Self-registration |
| 3 | Driver | Restaurant onboards |

### 7.3 Key Points

- Centralized authentication for all actors
- User type marker determines routing after login
- Driver ID inherently links to restaurant
- A customer cannot register as a restaurant (and vice versa)

---

## 8. Business Requirements Fulfillment

### 8.1 Customer Requirements

| Requirement | How Fulfilled |
|-------------|---------------|
| Create an account | Self-registration via Identity/Auth |
| Browse restaurants | Restaurant component (1 for MVP) |
| View menus | Menu component, shows availability |
| Add items to basket | Client-side basket → Order component |
| Place an order | Order component creates order |
| Pay for the order | Payment component (after restaurant accepts) |
| Track the order | Order state visible in tracking view |
| View previous orders | Order component history query |

### 8.2 Restaurant Requirements

| Requirement | How Fulfilled |
|-------------|---------------|
| Manage their menu | Menu component (add/edit/toggle availability) |
| Receive orders | Push notification + dashboard queue view |
| Accept or reject orders | Order state transition via Order component |
| Update the order status | Order component state management |
| See previous orders | Order component history query |

### 8.3 Driver Requirements

| Requirement | How Fulfilled |
|-------------|---------------|
| Become available for deliveries | Driver shift sign-in via Delivery component |
| Receive delivery requests | Restaurant assigns, driver notified |
| Accept a delivery | Acknowledge assignment (not accept/reject) |
| Update delivery status | Delivery component state updates |
| See current and previous deliveries | Delivery component queries |

### 8.4 Business Requirements

| Requirement | How Fulfilled |
|-------------|---------------|
| See orders and revenue | Reporting component - global aggregations |
| Know how many deliveries completed | Reporting component - delivery metrics |
| Basic reporting | Simple admin views, aggregated data |

---

## 9. Architectural Decisions

### 9.1 Decision 1: Modular Monolith with Shared Database

**What:** Single deployable application with clear internal module boundaries, shared database.

**Why:**
- 3 developers cannot manage multiple services
- No DevOps team to handle infrastructure
- Limited budget rules out complex infrastructure
- Need to prove business model quickly

**Trade-off:**
- Modules could become coupled if not disciplined
- Cannot scale components independently
- Will need to evaluate in Question 2 if growth requires splitting

---

### 9.2 Decision 2: Payment After Restaurant Acceptance

**What:** Payment is processed only after restaurant accepts the order, not at order submission.

**Why:**
- Avoids refund complexity if restaurant rejects
- Simpler payment state management
- Customer only charged for orders that will be fulfilled

**Trade-off:**
- Small window where restaurant accepts but payment fails (handled as edge case)
- Customer might abandon after acceptance but before payment (rare)

---

### 9.3 Decision 3: Drivers Owned by Restaurants, Not Platform

**What:** Drivers belong to restaurants (like pizza delivery), not a gig-economy pool.

**Why:**
- Matches the actual business model described
- Simpler system - no driver matching algorithm needed
- Restaurant manages their own staff
- Platform just provides the tools

**Trade-off:**
- Cannot do cross-restaurant driver optimization
- Each restaurant needs enough drivers for their volume
- Platform has less control over delivery experience

---

## 10. Risks

### 10.1 Risk 1: Module Coupling Over Time

**Risk:** Without discipline, modules in the monolith may become tangled through shared data or unclear boundaries.

**Mitigation:**
- Enforce module ownership of tables
- Use interfaces for cross-module calls
- Code review for boundary violations
- Folder structure enforces separation

**Monitoring:** Code reviews, periodic architecture reviews

---

### 10.2 Risk 2: Payment Provider Dependency

**Risk:** Single external dependency that can fail, affecting all orders.

**Mitigation:**
- Wrapped in Payment module (can swap providers)
- Simple failure handling for MVP
- Manual intervention possible at small scale

**Monitoring:** Payment success/failure rates

**Note:** Question 3 will require more robust handling

---

### 10.3 Risk 3: Single Database Performance

**Risk:** As data grows, single database may become bottleneck.

**Mitigation:**
- Acceptable for MVP scale (1 restaurant)
- Database indexing best practices
- Monitor query performance

**When to act:** If queries become slow in Question 2 growth scenario

---

## 11. Deliberate Omissions

### 11.1 Not Done: Real-Time GPS Tracking

**What we're NOT doing:** Live tracking of driver location on a map.

**Why not:**
- Adds significant complexity (location streaming, map integration)
- Not required for proving business model
- Driver can call/knock on arrival
- Status updates (in route, delivered) sufficient for MVP

**When to reconsider:** If customers demand it, or delivery disputes increase

---

### 11.2 Not Done: Customer Satisfaction / Rating System

**What we're NOT doing:** Post-delivery ratings, reviews, or feedback collection.

**Why not:**
- Adds entities (ratings, reviews)
- Adds UI flows
- 1 restaurant doesn't need competitive differentiation
- Disputes can be handled manually via support

**When to reconsider:** When multiple restaurants compete (Question 2+)

---

## Summary

### What We Have Designed

1. **8 Components** with clear responsibilities and data ownership
2. **Simple architecture** appropriate for constraints (3 devs, limited budget, MVP)
3. **Complete user flows** for Customer, Restaurant, and Driver
4. **Order lifecycle** with defined states and transitions
5. **External integration pattern** for payment provider
6. **Authentication system** with user type routing

### Ready for Question 2

The architecture is intentionally simple but has clear boundaries. This means:
- We can identify pain points as they emerge
- We can split modules if needed
- We can add complexity when justified
- We haven't over-engineered for hypothetical problems
