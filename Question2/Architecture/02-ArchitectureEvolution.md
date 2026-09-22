# Architecture Evolution: Q1 → Q2

## Document Purpose

This document details the specific changes to our architecture as we evolve from Q1 to Q2. Based on the Problem Analysis, we make informed decisions about what changes, why, and how the Q2 architecture differs from Q1.

**Guiding Principle:** Augment, don't replace. The Q1 foundation is sound.

---

## Table of Contents

1. [Evolution Overview](#1-evolution-overview)
2. [Change 1: Async Notifications (RabbitMQ)](#2-change-1-async-notifications-rabbitmq)
3. [Change 2: Caching Layer (Redis)](#3-change-2-caching-layer-redis)
4. [Change 3: Reporting Separation (Read Replica)](#4-change-3-reporting-separation-read-replica)
5. [Change 4: Deployment Evolution (Feature Flags)](#5-change-4-deployment-evolution-feature-flags)
6. [Change 5: Database Optimization](#6-change-5-database-optimization)
7. [Change 6: Team Process (Module Ownership)](#7-change-6-team-process-module-ownership)
8. [Q2 Architecture Diagram](#8-q2-architecture-diagram)
9. [What Remains Unchanged](#9-what-remains-unchanged)
10. [Migration Considerations](#10-migration-considerations)

---

## 1. Evolution Overview

### 1.1 Summary of Changes

| Change | Type | Problem Solved | Complexity |
|--------|------|----------------|------------|
| RabbitMQ for notifications | Add infrastructure | Notifications blocking ops | Medium |
| Redis caching | Add infrastructure | Browsing traffic overload | Medium |
| PostgreSQL read replica | Add infrastructure | Reporting affecting performance | Low-Medium |
| Feature flags | Add capability | Disruptive deployments | Low |
| Database optimization | Improve existing | Expensive DB operations | Low |
| Module ownership | Process change | Developer conflicts | None (process) |

### 1.2 Infrastructure Additions

**Q1 Infrastructure:**
- Application server(s)
- PostgreSQL database
- Push notification service (external)
- Payment provider (external)

**Q2 Infrastructure (additions in bold):**
- Application server(s)
- PostgreSQL database (primary)
- **PostgreSQL read replica**
- **Redis cache**
- **RabbitMQ message broker**
- **Background worker process(es)**
- Push notification service (external)
- Payment provider (external)

### 1.3 Cost Implications

| Component | Estimated Monthly Cost | Notes |
|-----------|----------------------|-------|
| PostgreSQL Replica | $50-200 | Same size as primary, cloud managed |
| Redis | $25-100 | Managed Redis (ElastiCache, etc.) |
| RabbitMQ | $25-100 | Managed or self-hosted |
| Background Workers | Minimal | Same servers, separate process |
| **Total Addition** | **~$100-400/month** | Reasonable for Q2 budget |

---

## 2. Change 1: Async Notifications (RabbitMQ)

### 2.1 What Changes

**Q1 (Synchronous):**
```
Place Order Request
    │
    ├── 1. Validate order
    ├── 2. Save to database
    ├── 3. Send restaurant notification ◀── WAIT 200ms
    ├── 4. Send customer notification   ◀── WAIT 200ms
    │
    ▼
Response (after ~500ms+ for notifications)
```

**Q2 (Asynchronous):**
```
Place Order Request
    │
    ├── 1. Validate order
    ├── 2. Save to database
    ├── 3. Publish to notification queue ◀── ~5ms
    │
    ▼
Response (fast, ~50ms total)

         ... Meanwhile, in background ...

    Notification Worker
    ├── Consume from queue
    ├── Send restaurant notification
    ├── Send customer notification
    └── (Retry if failed)
```

### 2.2 Why This Change

| Q1 Rationale | Q2 Reality | Decision |
|--------------|------------|----------|
| Simple implementation | Causes user-perceived latency | Change required |
| Acceptable at low volume | Volume increased significantly | Change required |
| Notification failure was rare | Now affects more orders | Change required |

### 2.3 How It Integrates

```
┌─────────────────────────────────────────────────────────────────┐
│                  Q2 NOTIFICATION ARCHITECTURE                    │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────┐
│  Order Module   │
│                 │
│  placeOrder()   │
│       │         │
│       ├── save to DB
│       │
│       └── publish to RabbitMQ ─────┐
│                 │                  │
│       ▼         │                  │
│  return success │                  │
└─────────────────┘                  │
                                     │
                                     ▼
                        ┌─────────────────────────────┐
                        │         RabbitMQ            │
                        │                             │
                        │  Exchange: notifications    │
                        │  Queues:                    │
                        │  - customer_notifications   │
                        │  - restaurant_notifications │
                        └──────────────┬──────────────┘
                                       │
                        ┌──────────────┴──────────────┐
                        │                             │
                        ▼                             ▼
              ┌─────────────────┐           ┌─────────────────┐
              │ Customer Worker │           │ Restaurant      │
              │                 │           │ Worker          │
              │ - Process msg   │           │                 │
              │ - Send push     │           │ - Process msg   │
              │ - Ack/Nack      │           │ - Send push     │
              └─────────────────┘           └─────────────────┘
```

### 2.4 Q1 Code → Q2 Code

**Q1 Order Service (synchronous):**
```javascript
async placeOrder(orderData) {
  const order = await this.orderRepository.save(orderData);

  // Blocking - user waits
  await this.notificationService.notifyRestaurant(order);
  await this.notificationService.notifyCustomer(order);

  return order;
}
```

**Q2 Order Service (async):**
```javascript
async placeOrder(orderData) {
  const order = await this.orderRepository.save(orderData);

  // Non-blocking - returns immediately
  await this.messageQueue.publish('notifications', {
    type: 'ORDER_PLACED',
    orderId: order.id,
    customerId: order.customerId,
    restaurantId: order.restaurantId
  });

  return order;
}
```

### 2.5 Events Published

| Event | Trigger | Data |
|-------|---------|------|
| `ORDER_PLACED` | Customer places order | orderId, customerId, restaurantId |
| `ORDER_ACCEPTED` | Restaurant accepts | orderId, customerId, estimatedTime |
| `ORDER_REJECTED` | Restaurant rejects | orderId, customerId, reason |
| `ORDER_READY` | Food ready for pickup | orderId, customerId, driverId |
| `DRIVER_ASSIGNED` | Driver assigned | orderId, customerId, driverId |
| `OUT_FOR_DELIVERY` | Driver en route | orderId, customerId |
| `ORDER_DELIVERED` | Delivery complete | orderId, customerId |

### 2.6 Failure Handling

| Scenario | Handling |
|----------|----------|
| Queue unavailable | Log error, continue (order still saved) |
| Worker fails | Message requeued, retried |
| Push service down | Exponential backoff retry |
| Max retries exceeded | Dead letter queue for investigation |

---

## 3. Change 2: Caching Layer (Redis)

### 3.1 What Changes

**Q1 (No cache):**
```
Browse Restaurants
    │
    ▼
Database query every time
    │
    ▼
Return data
```

**Q2 (With cache):**
```
Browse Restaurants
    │
    ▼
Check Redis cache
    │
    ├── HIT: Return cached data (fast, ~1ms)
    │
    └── MISS: Query database
              Store in cache
              Return data
```

### 3.2 Why This Change

| Q1 Rationale | Q2 Reality | Decision |
|--------------|------------|----------|
| Low traffic, DB handles it | High read traffic overwhelms DB | Change required |
| Real-time data needed | Most data stable (restaurant info) | Caching appropriate |
| Simple architecture | Simplicity causing performance issues | Worth the complexity |

### 3.3 Cache Strategy (Hybrid)

Based on our research and the real-time availability requirement:

| Data Type | TTL | Invalidation | Key Pattern |
|-----------|-----|--------------|-------------|
| Restaurant list | 5 minutes | On restaurant update | `restaurants:city:{cityId}` |
| Restaurant details | 5 minutes | On update | `restaurant:{id}` |
| Menu items | 2 minutes | On update | `menu:{restaurantId}` |
| Item availability | 30 seconds | Event-driven + TTL | `availability:{restaurantId}` |

### 3.4 Integration with Async (Bonus)

Since we're adding RabbitMQ, we can use it for cache invalidation:

```
┌─────────────────────────────────────────────────────────────────┐
│               CACHE + MESSAGE QUEUE INTEGRATION                  │
└─────────────────────────────────────────────────────────────────┘

Menu Service updates item availability
    │
    ├── 1. Update database
    │
    └── 2. Publish 'MENU_UPDATED' event
              │
              ▼
         ┌─────────────┐
         │  RabbitMQ   │
         └──────┬──────┘
                │
    ┌───────────┼───────────┐
    │           │           │
    ▼           ▼           ▼
┌───────┐  ┌───────┐  ┌───────────┐
│ Cache │  │ Notif │  │ Analytics │
│Listener│ │Worker │  │  (future) │
│       │  │       │  │           │
│Invalidate│ │ Push  │  │   Log    │
│ cache  │  │       │  │           │
└───────┘  └───────┘  └───────────┘
```

### 3.5 Cache Flow Code

```javascript
// Restaurant service with caching
class RestaurantService {
  constructor(repository, cache) {
    this.repository = repository;
    this.cache = cache;
  }

  async getRestaurants(cityId) {
    const cacheKey = `restaurants:city:${cityId}`;

    // Try cache first
    const cached = await this.cache.get(cacheKey);
    if (cached) {
      return JSON.parse(cached);
    }

    // Cache miss - fetch from DB
    const restaurants = await this.repository.findByCity(cityId);

    // Store in cache with TTL
    await this.cache.setex(cacheKey, 300, JSON.stringify(restaurants)); // 5 min

    return restaurants;
  }

  async updateRestaurant(id, data) {
    const restaurant = await this.repository.update(id, data);

    // Invalidate cache
    await this.cache.del(`restaurant:${id}`);
    await this.cache.del(`restaurants:city:${restaurant.cityId}`);

    // Publish event for other listeners
    await this.messageQueue.publish('cache_invalidation', {
      type: 'RESTAURANT_UPDATED',
      restaurantId: id
    });

    return restaurant;
  }
}
```

---

## 4. Change 3: Reporting Separation (Read Replica)

### 4.1 What Changes

**Q1 (Single database):**
```
┌─────────────────────────────────────────┐
│           PostgreSQL                     │
│                                         │
│   OLTP (orders)  +  OLAP (reports)     │
│        ↓                ↓               │
│        └────────┬───────┘               │
│                 │                       │
│           CONTENTION                    │
└─────────────────────────────────────────┘
```

**Q2 (Read replica):**
```
┌─────────────────────┐     ┌─────────────────────┐
│  PostgreSQL Primary │     │  PostgreSQL Replica │
│                     │────▶│                     │
│   OLTP (orders)     │ WAL │   OLAP (reports)   │
│                     │     │                     │
│   Writes + Reads    │     │   Reads only       │
└─────────────────────┘     └─────────────────────┘
```

### 4.2 Why This Change

| Q1 Rationale | Q2 Reality | Decision |
|--------------|------------|----------|
| Small data, fast reports | 6 months of data, slow reports | Change required |
| Single DB is simple | Reports blocking orders | Change required |
| No operational overhead | Overhead justified by problem | Change required |

### 4.3 What Routes Where

| Query Type | Database | Reason |
|------------|----------|--------|
| Place order | Primary | Write operation |
| Get order by ID | Primary | Needs latest data |
| Update order status | Primary | Write operation |
| Revenue report | Replica | Heavy aggregation |
| Order history | Replica | Read-heavy, slight lag OK |
| Dashboard metrics | Replica | Aggregations |
| Customer profile | Primary | May need latest |

### 4.4 Code Changes

```javascript
// Database configuration
const primaryDB = new Pool({
  host: process.env.DB_PRIMARY_HOST,
  // ... connection details
});

const replicaDB = new Pool({
  host: process.env.DB_REPLICA_HOST,
  // ... connection details
});

// Repository pattern
class OrderRepository {
  async save(order) {
    return primaryDB.query('INSERT INTO orders...', [order]);
  }

  async findById(id) {
    return primaryDB.query('SELECT * FROM orders WHERE id = $1', [id]);
  }
}

// Reporting repository - separate
class ReportingRepository {
  async getRevenueByRestaurant(startDate, endDate) {
    // Uses replica - heavy query won't affect orders
    return replicaDB.query(`
      SELECT restaurant_id, SUM(total_amount) as revenue
      FROM orders
      WHERE created_at BETWEEN $1 AND $2
      GROUP BY restaurant_id
    `, [startDate, endDate]);
  }
}
```

### 4.5 Materialized Views (Enhancement)

Add materialized views on replica for common reports:

```sql
-- On replica database
CREATE MATERIALIZED VIEW daily_revenue AS
SELECT
  restaurant_id,
  DATE(created_at) as order_date,
  COUNT(*) as order_count,
  SUM(total_amount) as revenue
FROM orders
WHERE state = 'COMPLETED'
GROUP BY restaurant_id, DATE(created_at);

-- Refresh every hour
-- (scheduled job)
REFRESH MATERIALIZED VIEW CONCURRENTLY daily_revenue;
```

### 4.6 Replication Lag Handling

```javascript
// For critical reads that need latest data
async function getOrderStatus(orderId) {
  // Use primary - needs to be real-time
  return primaryDB.query('SELECT state FROM orders WHERE id = $1', [orderId]);
}

// For reports that can tolerate lag
async function getDailyRevenue(restaurantId) {
  // Use replica - slight lag is fine for reports
  return replicaDB.query(
    'SELECT * FROM daily_revenue WHERE restaurant_id = $1',
    [restaurantId]
  );
}
```

---

## 5. Change 4: Deployment Evolution (Feature Flags)

### 5.1 What Changes

**Q1 (Deploy = Release):**
```
Merge code → Build → Deploy → ALL users see change immediately
```

**Q2 (Deploy ≠ Release):**
```
Merge code (with flag) → Build → Deploy → Feature OFF for all
    │
    └── Enable for 10% → Monitor → Enable for 100%
```

### 5.2 Why This Change

| Q1 Rationale | Q2 Reality | Decision |
|--------------|------------|----------|
| Small user base, low risk | Larger user base, higher risk | Should change |
| Simple process | Process still simple with flags | Worth it |
| Quick rollback via redeploy | Instant rollback via flag | Better |

### 5.3 Implementation Approach

**Simple database-backed flags:**

```sql
-- Feature flags table
CREATE TABLE feature_flags (
  name VARCHAR(100) PRIMARY KEY,
  enabled BOOLEAN DEFAULT false,
  rollout_percentage INT DEFAULT 0,
  description TEXT,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);
```

**Feature flag service:**

```javascript
class FeatureFlagService {
  constructor(db, cache) {
    this.db = db;
    this.cache = cache;
  }

  async isEnabled(flagName, userId = null) {
    // Check cache first
    const cacheKey = `feature_flag:${flagName}`;
    let flag = await this.cache.get(cacheKey);

    if (!flag) {
      const result = await this.db.query(
        'SELECT * FROM feature_flags WHERE name = $1',
        [flagName]
      );
      flag = result.rows[0];
      if (flag) {
        await this.cache.setex(cacheKey, 60, JSON.stringify(flag)); // 1 min cache
      }
    } else {
      flag = JSON.parse(flag);
    }

    if (!flag || !flag.enabled) return false;

    // Percentage rollout
    if (userId && flag.rollout_percentage < 100) {
      const bucket = this.hashUserId(userId) % 100;
      return bucket < flag.rollout_percentage;
    }

    return true;
  }

  hashUserId(userId) {
    // Simple hash for consistent bucketing
    let hash = 0;
    for (let i = 0; i < userId.length; i++) {
      hash = ((hash << 5) - hash) + userId.charCodeAt(i);
      hash |= 0;
    }
    return Math.abs(hash);
  }
}
```

**Usage in code:**

```javascript
async function checkout(order, userId) {
  if (await featureFlags.isEnabled('new_checkout_flow', userId)) {
    return newCheckoutFlow(order);
  } else {
    return oldCheckoutFlow(order);
  }
}
```

### 5.4 Flag Lifecycle

```
1. Create flag (disabled): new_payment_ui
2. Deploy code with flag checks
3. Enable for internal team (whitelist)
4. Enable for 10% of users
5. Monitor metrics
6. Expand to 50%, then 100%
7. Remove flag and old code path
```

---

## 6. Change 5: Database Optimization

### 6.1 What Changes

**Q1:** Basic indexing, unoptimized queries
**Q2:** Comprehensive indexing, query optimization

### 6.2 Why This Change

This is maintenance, not architecture change. But it's necessary as data grows.

### 6.3 Index Audit

| Table | Column(s) | Index Type | Purpose |
|-------|-----------|------------|---------|
| orders | restaurant_id | B-tree | Filter by restaurant |
| orders | customer_id | B-tree | Order history |
| orders | state | B-tree | Queue views |
| orders | created_at | B-tree | Date range queries |
| orders | (restaurant_id, created_at) | Composite | Restaurant reports |
| order_items | order_id | B-tree | Order details lookup |
| menu_items | restaurant_id | B-tree | Menu by restaurant |
| deliveries | driver_id | B-tree | Driver history |
| deliveries | order_id | B-tree | Order tracking |

### 6.4 Query Optimization Practices

```sql
-- BEFORE: No index, full scan
SELECT * FROM orders WHERE restaurant_id = 123;

-- AFTER: With index
CREATE INDEX idx_orders_restaurant_id ON orders(restaurant_id);

-- Use EXPLAIN ANALYZE to verify
EXPLAIN ANALYZE SELECT * FROM orders WHERE restaurant_id = 123;
```

---

## 7. Change 6: Team Process (Module Ownership)

### 7.1 What Changes

**Q1:** Informal ownership, everyone works everywhere
**Q2:** Defined module owners, clear responsibilities

### 7.2 Why This Change

| Q1 Rationale | Q2 Reality | Decision |
|--------------|------------|----------|
| 3 devs, informal works | 5 devs, more conflicts | Should change |
| Everyone knows everything | Knowledge spreading thin | Need ownership |
| Quick coordination | Slower, more overhead | Structure helps |

### 7.3 Module Ownership Model

| Module | Primary Owner | Secondary Owner |
|--------|---------------|-----------------|
| Identity/Auth | Dev A | Dev B |
| Customer | Dev A | Dev C |
| Restaurant | Dev B | Dev D |
| Menu | Dev B | Dev D |
| Order | Dev C | Dev E |
| Payment | Dev C | Dev A |
| Delivery | Dev D | Dev E |
| Reporting | Dev E | Dev B |

### 7.4 Process Rules

1. **Changes to a module** require review from module owner
2. **Cross-module changes** require review from both owners
3. **Shared code changes** require team discussion
4. **Module owner** responsible for:
   - Code quality in their module
   - Documentation
   - Onboarding others to the module

### 7.5 CODEOWNERS File

```
# .github/CODEOWNERS

# Identity/Auth
/src/modules/identity/ @dev-a @dev-b

# Customer
/src/modules/customer/ @dev-a @dev-c

# Restaurant & Menu
/src/modules/restaurant/ @dev-b @dev-d
/src/modules/menu/ @dev-b @dev-d

# Order & Payment
/src/modules/order/ @dev-c @dev-e
/src/modules/payment/ @dev-c @dev-a

# Delivery
/src/modules/delivery/ @dev-d @dev-e

# Reporting
/src/modules/reporting/ @dev-e @dev-b

# Shared infrastructure requires team review
/src/infrastructure/ @dev-a @dev-b @dev-c @dev-d @dev-e
```

---

## 8. Q2 Architecture Diagram

### 8.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Q2 ARCHITECTURE                                      │
└─────────────────────────────────────────────────────────────────────────────┘

                    ┌─────────────────────────────┐
                    │          CLIENTS            │
                    │                             │
                    │  Customer   Restaurant  Driver │
                    │    App       Dashboard   App  │
                    └──────────────┬──────────────┘
                                   │
                                   │ HTTPS/REST
                                   ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│                         MODULAR MONOLITH                                    │
│                                                                             │
│   ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐              │
│   │Identity │ │Customer │ │Restaurant│ │  Menu   │ │  Order  │              │
│   │  Auth   │ │         │ │         │ │         │ │         │              │
│   └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘              │
│        │           │           │           │           │                    │
│   ┌────┴────┐ ┌────┴────┐ ┌────┴────┐                                      │
│   │ Payment │ │Delivery │ │Reporting│                                      │
│   └────┬────┘ └────┬────┘ └────┬────┘                                      │
│        │           │           │                                            │
│   ─────┴───────────┴───────────┴────────────────────────────────────────   │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐  │
│   │                     FEATURE FLAG SERVICE                             │  │
│   │                     (progressive rollout)                            │  │
│   └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
        │           │           │                   │
        │           │           │                   │
        │           │           │                   │
        ▼           ▼           ▼                   ▼
┌───────────┐ ┌───────────┐ ┌───────────┐   ┌───────────────┐
│   Redis   │ │PostgreSQL │ │PostgreSQL │   │   RabbitMQ    │
│   Cache   │ │  Primary  │ │  Replica  │   │               │
│           │ │           │ │           │   │  Exchanges:   │
│ - Menus   │ │ - OLTP    │ │ - Reports │   │  notifications│
│ - Lists   │ │ - Writes  │ │ - OLAP    │   │               │
│ - Flags   │ │           │ │           │   └───────┬───────┘
└───────────┘ └───────────┘ └───────────┘           │
                    │                               │
                    │ Streaming                     │
                    │ Replication                   │
                    └───────────────────────────────┼───────────────────┐
                                                    │                   │
                                                    ▼                   ▼
                                          ┌─────────────────┐ ┌─────────────────┐
                                          │   Notification  │ │   Notification  │
                                          │    Worker       │ │    Worker       │
                                          │   (Customer)    │ │  (Restaurant)   │
                                          └────────┬────────┘ └────────┬────────┘
                                                   │                   │
                                                   ▼                   ▼
                                          ┌─────────────────────────────────────┐
                                          │         Push Notification           │
                                          │           Service (FCM)             │
                                          └─────────────────────────────────────┘
```

### 8.2 Data Flow: Place Order

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      PLACE ORDER FLOW (Q2)                                  │
└─────────────────────────────────────────────────────────────────────────────┘

Customer                  Application                  Infrastructure
────────                  ───────────                  ──────────────
    │                          │                            │
    │  POST /orders            │                            │
    │ ────────────────────────▶│                            │
    │                          │                            │
    │                          │  1. Validate               │
    │                          │                            │
    │                          │  2. Save to DB ───────────▶│ PostgreSQL
    │                          │                            │ (primary)
    │                          │                            │
    │                          │  3. Publish event ────────▶│ RabbitMQ
    │                          │     (ORDER_PLACED)         │
    │                          │                            │
    │  Response (fast!)        │                            │
    │◀────────────────────────│                            │
    │                          │                            │
    │                          │        ... async ...       │
    │                          │                            │
    │                          │           Worker ◀─────────│ Consumes
    │                          │             │              │
    │                          │     Send push notification │
    │                          │             │              │
    │  Push notification       │◀────────────┘              │
    │  (arrives shortly)       │                            │
    │                          │                            │
```

### 8.3 Data Flow: Browse Restaurants

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    BROWSE RESTAURANTS FLOW (Q2)                             │
└─────────────────────────────────────────────────────────────────────────────┘

Customer                  Application                  Infrastructure
────────                  ───────────                  ──────────────
    │                          │                            │
    │  GET /restaurants        │                            │
    │ ────────────────────────▶│                            │
    │                          │                            │
    │                          │  1. Check Redis ──────────▶│ Redis Cache
    │                          │                            │
    │                          │  2a. CACHE HIT ◀──────────│
    │                          │      Return cached         │
    │                          │                            │
    │  Response (fast, ~10ms)  │                            │
    │◀────────────────────────│                            │
    │                          │                            │
    │         OR               │                            │
    │                          │                            │
    │                          │  2b. CACHE MISS            │
    │                          │      Query DB ────────────▶│ PostgreSQL
    │                          │                            │
    │                          │      Store in cache ──────▶│ Redis
    │                          │                            │
    │  Response (~50ms)        │                            │
    │◀────────────────────────│                            │
    │                          │                            │
```

---

## 9. What Remains Unchanged

### 9.1 Core Architecture

| Element | Status | Notes |
|---------|--------|-------|
| Modular Monolith | Unchanged | Still appropriate for 5 devs |
| 8 Module Structure | Unchanged | Business domains well-defined |
| Pragmatic Layered + DDD | Unchanged | Internal code organization |
| Shared Database Model | Unchanged | Adding replica, not splitting |

### 9.2 Business Logic

| Element | Status | Notes |
|---------|--------|-------|
| Order State Machine | Unchanged | Same states, same transitions |
| Payment Flow | Unchanged | Still after restaurant accepts |
| Driver Model | Unchanged | Still restaurant-owned |
| User Types | Unchanged | Customer, Restaurant, Driver |

### 9.3 External Integrations

| Element | Status | Notes |
|---------|--------|-------|
| Payment Provider Interface | Unchanged | Still wrapped in Payment module |
| Push Notification Service | Unchanged | Just calling it async now |

---

## 10. Migration Considerations

### 10.1 Order of Implementation

| Priority | Change | Dependencies | Risk |
|----------|--------|--------------|------|
| 1 | Database indexes | None | Low |
| 2 | Read replica setup | None | Low |
| 3 | Redis cache | None | Low |
| 4 | RabbitMQ + Workers | None | Medium |
| 5 | Feature flags | Redis (optional) | Low |
| 6 | Migrate notifications async | RabbitMQ | Medium |
| 7 | Module ownership | None (process) | None |

### 10.2 Migration Strategy

**Phase 1: Infrastructure (No code changes)**
- Set up PostgreSQL read replica
- Set up Redis
- Set up RabbitMQ
- Add database indexes

**Phase 2: Reporting (Low risk)**
- Route reporting queries to replica
- Add materialized views
- Verify reports work correctly

**Phase 3: Caching (Medium risk)**
- Add caching layer code
- Start with long TTL, conservative
- Monitor hit rates

**Phase 4: Async Notifications (Requires coordination)**
- Deploy workers
- Add queue publishing to order flow (feature flagged)
- Gradually enable
- Remove old sync notification code

**Phase 5: Cleanup**
- Remove feature flags for completed migrations
- Document new architecture
- Update runbooks

### 10.3 Rollback Plans

| Change | Rollback |
|--------|----------|
| Read replica | Route queries back to primary |
| Redis cache | Disable cache checks, fall through to DB |
| RabbitMQ | Feature flag back to sync notifications |
| Feature flags | Disable flag system, default to old behavior |

---

## Document Summary

**Q2 Evolution from Q1:**

| Aspect | Q1 | Q2 | Change Type |
|--------|----|----|-------------|
| Notifications | Synchronous | Async (RabbitMQ) | Add |
| Caching | None | Redis | Add |
| Reporting | Same DB | Read Replica | Add |
| Deployment | All-or-nothing | Feature flags | Add |
| Database | Basic indexes | Optimized | Improve |
| Team | Informal | Module ownership | Process |
| Architecture | Modular Monolith | Modular Monolith | Unchanged |
| Components | 8 modules | 8 modules | Unchanged |

**Key Takeaway:** The Q1 architecture was correct for Q1. The Q2 evolution adds capabilities to handle scale without replacing the foundation.

---

*Next Document: Technical Specifications - Implementation details*
