> **Superseded:** the final, reworked answers (modular monolith + vertical slices) are in [`FinalAnswers/`](FinalAnswers/README.md). This file is kept as history. See `FinalAnswers/CHANGES-AND-REASONS.md` for what changed and why.

# Question 2: Architecture Evolution - Answers

## Context: Six Months After Question 1

| Aspect | Q1 | Q2 |
|--------|----|----|
| Restaurants | 1 | 15 (expecting 50) |
| Drivers | Small number | More (still restaurant-owned) |
| Customers | Small base | Significantly larger |
| Developers | 3 | 5 |
| Budget | Very limited | More to invest |
| DevOps | None | Still none |
| Geography | One city | Still one city |

---

## 1. Problems Observed

### Problem 1: Restaurant Browsing Traffic

**Symptom:** Restaurant browsing receiving significantly more traffic than other parts of the application.

**Why This Happens (Q1 Architecture):**
- Restaurant and Menu modules hit same database as Order processing
- No caching layer
- Every browse query hits PostgreSQL directly
- Read-heavy operation competing with write operations

### Problem 2: Expensive Database Operations

**Symptom:** Some database operations becoming expensive.

**Why This Happens (Q1 Architecture):**
- Single PostgreSQL database for all modules
- Data volume has grown 15x (restaurants, orders, customers)
- No query optimization beyond basic indexing

### Problem 3: Notifications Blocking Operations

**Symptom:** Notifications slowing down certain operations.

**Why This Happens (Q1 Architecture):**
- Notifications are synchronous (utility service called directly)
- When order placed → wait for notification to send → then return response
- Push notification services have latency (100-500ms per call)

### Problem 4: Reporting Affecting Performance

**Symptom:** Reporting queries affecting normal application performance.

**Why This Happens (Q1 Architecture):**
- Reporting module reads directly from operational tables
- Complex aggregation queries (SUM, COUNT, GROUP BY) can lock tables
- Same database, same connection pool

### Problem 5: Disruptive Deployments

**Symptom:** Deployments becoming more disruptive.

**Why This Happens (Q1 Architecture):**
- Single monolith deployment = all-or-nothing
- Any change requires full redeploy
- More features = longer startup, more risk
- One bug can take down everything

### Problem 6: Developer Conflicts

**Symptom:** Developers increasingly working in same areas of codebase.

**Why This Happens (Q1 Architecture):**
- 5 developers on one codebase
- Module boundaries exist but may not be strict enough
- Shared code areas (common utilities, database layer)

### Problem 7: Rapid Restaurant Onboarding

**Symptom:** Business wants to add more restaurants rapidly.

**Why This Happens:**
- Current system supports multiple restaurants
- May need self-service onboarding (not platform-created accounts)
- Data isolation concerns with 50 restaurants

---

## 2. What Still Works from Q1?

| Component | Status | Reasoning |
|-----------|--------|-----------|
| **Modular Monolith Structure** | KEEP | 5 devs still manageable; module boundaries help |
| **Pragmatic Layered Architecture** | KEEP | Works well; team familiar |
| **8 Module Structure** | KEEP | Correct domain boundaries |
| **Order State Machine** | KEEP | Business logic unchanged |
| **Payment Flow (after accept)** | KEEP | Still correct business model |
| **Driver Model (restaurant-owned)** | KEEP | Business model unchanged |
| **PostgreSQL as primary database** | KEEP | Good choice; add replicas |

---

## 3. What Changes Are Needed?

### Change 1: Redis Caching Layer

**Solves:** Problem 1 (Restaurant Browsing Traffic), Problem 2 (Expensive Operations)

**What:**
- Add Redis cache for restaurant and menu data
- Use cache-aside pattern with event-driven invalidation
- Short TTL (5 minutes) as safety net

**Why This Solution:**
- Restaurant/menu data is read-heavy (100:1 read-to-write ratio)
- Data changes infrequently but needs immediate invalidation when it does
- Reduces database load by 80-90% for browse operations

**Implementation:**
```javascript
// Cache-aside with event invalidation
async getRestaurantMenu(restaurantId) {
  const cached = await redis.get(`menu:${restaurantId}`);
  if (cached) return JSON.parse(cached);

  const menu = await this.menuRepository.findByRestaurant(restaurantId);
  await redis.set(`menu:${restaurantId}`, JSON.stringify(menu), 'EX', 300);
  return menu;
}

// On menu update - invalidate immediately
async updateMenuItem(item) {
  await this.menuRepository.save(item);
  await redis.del(`menu:${item.restaurantId}`);
}
```

### Change 2: Async Notification Processing (RabbitMQ)

**Solves:** Problem 3 (Notifications Blocking Operations)

**What:**
- Add RabbitMQ message queue
- Order placement publishes event to queue
- Background worker processes notifications
- Main request returns immediately

**Why RabbitMQ:**
- Full message broker with retries and dead letter queues
- Better observability than Redis Queue
- Supports multiple exchange patterns (direct, fanout, topic)
- Industry standard for this use case

**Implementation:**
```javascript
// Before (Q1 - synchronous)
async placeOrder(order) {
  await this.orderRepository.save(order);
  await this.notificationService.sendPush(order.restaurantId, 'New order!'); // BLOCKS
  return order;
}

// After (Q2 - asynchronous)
async placeOrder(order) {
  await this.orderRepository.save(order);
  await this.messageQueue.publish('notifications', {
    type: 'ORDER_PLACED',
    orderId: order.id,
    restaurantId: order.restaurantId
  });
  return order; // Returns immediately
}

// Background worker
async processNotification(message) {
  const { type, orderId, restaurantId } = message;
  await this.notificationService.sendPush(restaurantId, 'New order!');
}
```

**Queue Configuration:**
- Exchange: `notifications` (topic exchange)
- Routing keys: `order.placed`, `order.ready`, `delivery.assigned`
- Dead letter queue for failed notifications (retry 3 times)
- Prefetch: 10 messages per worker

### Change 3: PostgreSQL Read Replica for Reporting

**Solves:** Problem 4 (Reporting Affecting Performance)

**What:**
- Add PostgreSQL read replica using streaming replication
- Route all reporting queries to replica
- Operational queries stay on primary

**Why Read Replica:**
- Simpler than CQRS or separate data warehouse
- PostgreSQL streaming replication is built-in
- Acceptable lag for reports (seconds, not real-time)
- Minimal code changes

**Implementation:**
```javascript
// Reporting module uses replica connection
class ReportingRepository {
  constructor() {
    this.connection = Database.getReplicaConnection(); // Not primary
  }

  async getTotalOrdersThisMonth(restaurantId) {
    return this.connection.query(`
      SELECT COUNT(*) as total, SUM(total_amount) as revenue
      FROM orders
      WHERE restaurant_id = $1 AND created_at >= $2
    `, [restaurantId, startOfMonth]);
  }
}
```

### Change 4: Database Optimization

**Solves:** Problem 2 (Expensive Database Operations)

**What:**
- Add missing indexes based on query analysis
- Tune connection pool settings
- Add covering indexes for frequent queries

**Key Indexes to Add:**
```sql
-- Orders by restaurant and state (for queue views)
CREATE INDEX idx_orders_restaurant_state ON orders(restaurant_id, state);

-- Orders by customer (for order history)
CREATE INDEX idx_orders_customer ON orders(customer_id, created_at DESC);

-- Menu items by restaurant (for menu fetching)
CREATE INDEX idx_menu_items_restaurant ON menu_items(restaurant_id, is_available);

-- Deliveries by driver and date (for driver history)
CREATE INDEX idx_deliveries_driver ON deliveries(driver_id, assigned_at DESC);
```

### Change 5: Feature Flags

**Solves:** Problem 5 (Disruptive Deployments) - Partial

**What:**
- Add feature flag system
- New features deployed but disabled by default
- Gradual rollout to restaurants
- Quick disable if issues found

**Why Feature Flags:**
- Decouples deployment from release
- Reduces deployment risk
- Enables A/B testing
- Still single deployment unit (no microservices overhead)

**Implementation:**
```javascript
// Feature flag check
async showNewMenuLayout(restaurantId) {
  const flag = await this.featureFlags.get('new_menu_layout');

  if (flag.enabled && flag.restaurants.includes(restaurantId)) {
    return this.renderNewLayout();
  }
  return this.renderOldLayout();
}

// Flag configuration
{
  "new_menu_layout": {
    "enabled": true,
    "restaurants": ["rest_001", "rest_002"], // Gradual rollout
    "percentage": 20 // Or percentage-based
  }
}
```

### Change 6: Strengthen Module Boundaries

**Solves:** Problem 6 (Developer Conflicts)

**What:**
- Enforce module ownership (CODEOWNERS file)
- Each module has designated owner(s)
- Cross-module changes require review from both owners
- Separate test suites per module

**Why This Works:**
- Reduces merge conflicts
- Clear ownership reduces confusion
- Prepares for potential future extraction
- Process change, not architectural

**Implementation:**
```
# CODEOWNERS
/src/modules/order/       @alice @bob
/src/modules/payment/     @alice
/src/modules/restaurant/  @charlie
/src/modules/menu/        @charlie
/src/modules/delivery/    @bob @david
/src/modules/customer/    @eve
/src/modules/identity/    @eve @alice
/src/modules/reporting/   @david
```

---

## 4. What Should NOT Change?

| Area | Keep As-Is | Reasoning |
|------|------------|-----------|
| **Modular Monolith** | Yes | 5 devs not ready for microservices overhead |
| **Database per module** | No - Keep shared | Not enough scale to justify |
| **Service mesh** | No | Massive overkill |
| **Full event sourcing** | No | Only need async for notifications |
| **Kubernetes** | Not yet | Simple deployment still works |
| **Multi-region** | No | Still one city |
| **Driver matching algorithm** | No | Drivers still restaurant-owned |

---

## 5. Q2 Architecture Summary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         MODULAR MONOLITH                                    │
│                                                                             │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐          │
│   │Identity │  │Customer │  │Restaurant│ │  Menu   │  │  Order  │          │
│   │  Auth   │  │         │  │         │  │         │  │         │          │
│   └─────────┘  └─────────┘  └─────────┘  └─────────┘  └─────────┘          │
│                                                                             │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐                                    │
│   │ Payment │  │Delivery │  │Reporting│                                    │
│   │         │  │         │  │         │                                    │
│   └─────────┘  └─────────┘  └─────────┘                                    │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐  │
│   │                    NEW: MESSAGE QUEUE (RabbitMQ)                     │  │
│   │                    Async Notifications                               │  │
│   └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
        │                    │                              │
        ▼                    ▼                              ▼
┌───────────────┐    ┌───────────────┐              ┌───────────────┐
│   NEW: Redis  │    │  PostgreSQL   │──replicates─▶│ NEW: Read     │
│   Cache       │    │  (Primary)    │              │ Replica       │
│               │    │               │              │ (Reporting)   │
└───────────────┘    └───────────────┘              └───────────────┘

┌───────────────┐
│ NEW: Background│
│ Worker        │
│ (Notifications)│
└───────────────┘
```

---

## 6. Problem-to-Solution Mapping

| Problem | Solution | Why This Solution |
|---------|----------|-------------------|
| Restaurant browsing traffic | Redis cache | High read-to-write ratio; data changes infrequently |
| Expensive database operations | Index optimization + caching | Low-hanging fruit with high impact |
| Notifications blocking | RabbitMQ async processing | Notifications not on critical path |
| Reporting affecting performance | PostgreSQL read replica | Separates OLTP from OLAP workloads |
| Disruptive deployments | Feature flags | Decouples deployment from release |
| Developer conflicts | CODEOWNERS + module ownership | Process change reduces friction |
| Rapid restaurant onboarding | Operational (not architectural) | Current architecture supports it |

---

## 7. Implementation Priority

| Priority | Change | Effort | Impact | Why This Order |
|----------|--------|--------|--------|----------------|
| 1 | Database indexes | Low | High | Fastest win, no new infrastructure |
| 2 | RabbitMQ (async notifications) | Medium | High | Removes user-facing latency |
| 3 | Redis caching | Medium | High | Reduces DB load significantly |
| 4 | Read replica | Low-Medium | High | Frees operational DB from reporting |
| 5 | CODEOWNERS | Low | Medium | Process, not technical |
| 6 | Feature flags | Medium | Medium | Risk reduction for deployments |

---

## 8. Decisions Made

### Decision 1: RabbitMQ for Async Processing

**Choice:** RabbitMQ over Redis Queue or database-backed queue

**Why:**
- Full message broker with built-in retry mechanisms
- Dead letter queues for failed messages
- Better observability (management UI)
- Supports complex routing patterns
- Industry standard

### Decision 2: Cache-Aside with Event Invalidation

**Choice:** Cache-aside pattern with event-driven invalidation (not write-through)

**Why:**
- Menu data changes in real-time (items go unavailable immediately)
- Write-through would add latency to writes
- Event invalidation ensures immediate consistency
- Short TTL as safety net

### Decision 3: Read Replica over CQRS

**Choice:** PostgreSQL read replica over separate reporting database

**Why:**
- Simpler than full CQRS
- Built-in PostgreSQL feature (streaming replication)
- Minimal code changes
- Acceptable lag for reports (seconds)
- Can evolve to CQRS later if needed

### Decision 4: Feature Flags over Blue-Green

**Choice:** Feature flags for deployment risk mitigation

**Why:**
- Simpler than blue-green deployment infrastructure
- Allows per-restaurant rollout
- Quick rollback by toggling flag
- Still single deployment unit

---

## 9. What We're NOT Doing (And Why)

| Option Rejected | Why |
|-----------------|-----|
| Microservices | 5 devs cannot manage distributed system complexity |
| Kubernetes | No DevOps; simple deployment still works |
| Full CQRS | Overkill; read replica solves the problem |
| Event Sourcing | Only need async for notifications, not full event store |
| Database per module | Not enough scale to justify operational overhead |
| Service mesh | Massive overkill for current scale |
| Multi-region | Still one city |

---

## 10. Comparison: Q1 vs Q2

| Aspect | Q1 | Q2 |
|--------|----|----|
| Deployment | Modular Monolith | Modular Monolith (unchanged) |
| Internal Architecture | Pragmatic Layered + DDD | Pragmatic Layered + DDD (unchanged) |
| Database | Single PostgreSQL | Primary + Read Replica |
| Caching | None | Redis for read-heavy data |
| Notifications | Synchronous | Asynchronous (RabbitMQ) |
| Deployment Risk | All-or-nothing | Feature flags |
| Team Process | Informal | CODEOWNERS + module ownership |

---

## 11. New Infrastructure

| Component | Purpose | Technology |
|-----------|---------|------------|
| **Message Queue** | Async notifications | RabbitMQ |
| **Cache** | Restaurant/menu data | Redis |
| **Read Replica** | Reporting queries | PostgreSQL streaming replication |
| **Background Worker** | Process notifications | Same runtime, separate process |
| **Feature Flag Store** | Toggle features | Simple config or Unleash/LaunchDarkly |

---

## 12. Guiding Principle

**Solve Q2's problems for Q2's constraints.**

- Do NOT design for hypothetical Q3
- Each solution must be justified by a Q2 problem
- If it helps Q3 too, that's a bonus - not the goal
- Building for maintainability inherently supports future scalability
- But we don't add complexity "just in case"

---

## Summary

Q2 is about **augmenting** the Q1 architecture, not replacing it. The modular monolith remains appropriate for 5 developers and 15 restaurants. We add targeted infrastructure (Redis, RabbitMQ, read replica) to solve specific performance problems while keeping the core architecture simple.

The key changes are:
1. **Caching** - Reduce database load for read-heavy operations
2. **Async processing** - Remove blocking I/O from critical paths
3. **Read replica** - Separate reporting workload from operations
4. **Feature flags** - Reduce deployment risk
5. **Process changes** - Module ownership to reduce conflicts
