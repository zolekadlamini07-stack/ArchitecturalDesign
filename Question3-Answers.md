# Question 3: Architecture at Scale - Answers

## Context: Scaling Beyond Q2

| Aspect | Q1 | Q2 | Q3 |
|--------|----|----|----|
| Restaurants | 1 | 15 (expecting 50) | 50 (expecting more) |
| Drivers | Small number | More (restaurant-owned) | Restaurant-owned, larger pool |
| Customers | Small base | Significantly larger | Large, multi-city |
| Developers | 3 | 5 | 12, organized into 2 teams |
| Budget | Very limited | More to invest | Established, revenue-backed |
| DevOps | None | None | Minimal - just enough for 2 deployables |
| Geography | One city | Still one city | Possibly multiple cities |

---

## 1. Problems Observed

### Problem 1: Payment Provider Failures Cascade Into Orders

**Symptom:** When Stripe has a slow period or outage, orders get stuck in `ACCEPTED` state with no clean recovery path; retries are ad-hoc and sometimes double-charge customers.

**Why This Happens (Q2 Architecture):**
- Payment logic lives inside the monolith, called in-process from the Order module
- No circuit breaker - every request waits on Stripe regardless of its health
- No idempotency key - a retried request can create a second charge
- Failure handling is scattered across whatever code path happened to call Payment

### Problem 2: Payment Changes Require Coordinating With the Whole Monolith

**Symptom:** Both teams touch the Payment module indirectly (Order team calls it, Platform team maintains it), and any payment fix requires a full monolith redeploy.

**Why This Happens:**
- Payment is still just another module in the shared deployable
- No independent release cycle - a payment hotfix ships with everything else
- 12 developers across 2 teams now routinely block on each other's unrelated changes

### Problem 3: Two Teams, One Codebase, Blurring Ownership

**Symptom:** CODEOWNERS (from Q2) slows conflicts but doesn't stop them - teams still negotiate over shared infrastructure code and cross-module interfaces.

**Why This Happens:**
- Module boundaries are logical, not physical - everything still deploys and scales together
- At 5 developers this was tolerable; at 12 across 2 teams it's a recurring source of PR back-and-forth

### Problem 4: Restaurant Onboarding Is Still Manual

**Symptom:** Platform staff still create restaurant accounts by hand; 50+ restaurants (and growing) makes this a bottleneck.

**Why This Happens:**
- Q1/Q2 never needed self-service onboarding at small scale
- Restaurant module has no self-registration flow, no approval workflow

### Problem 5: Notification Worker Is a Single Point of Throughput Limit

**Symptom:** At higher order volume, the single notification worker (from Q2) occasionally lags during peak hours.

**Why This Happens:**
- One consumer process on the RabbitMQ queue from Q2
- No horizontal scaling of workers

---

## 2. What Still Works from Q2?

| Component | Status | Reasoning |
|-----------|--------|-----------|
| **Modular Monolith (minus Payment)** | KEEP | Still the right fit for Order, Menu, Restaurant, Delivery, Customer, Identity, Reporting |
| **Redis Caching** | KEEP | Still solves the same read-heavy problem, now at larger scale |
| **RabbitMQ** | KEEP | Reuse it as the backbone for Payment extraction too, not a new broker |
| **PostgreSQL Primary + Read Replica** | KEEP | Reporting workload separation still valid |
| **CODEOWNERS / Module Ownership** | KEEP, extend | Still useful inside the monolith; extend to match the 2-team split |
| **Feature Flags** | KEEP | Deployment risk management still needed, now across two deployables |
| **Driver Model (restaurant-owned)** | KEEP | Business model unchanged |

---

## 3. What Changes Are Needed?

### Change 1: Extract Payment Into Its Own Service

**Solves:** Problem 1 (Provider Failures Cascade), Problem 2 (Coordinated Deploys)

**What:**
- Payment becomes a separately deployed service with its own database (payments, payment_attempts tables)
- Order module no longer calls Payment in-process; it publishes a `ProcessPayment` command over RabbitMQ and reacts to `PaymentSucceeded` / `PaymentFailed` events
- This is the **one and only** service extracted in Q3 - not a jump to microservices everywhere

**Why Payment Specifically:**
- It's the module with a genuinely different failure mode (external, third-party, network-bound) from the rest of the system
- It's the module both teams depend on but neither fully owns - extraction resolves the ownership ambiguity
- Q2's RabbitMQ is already in place, so this is additive infrastructure, not new infrastructure

**Implementation:**
```javascript
// Order module (in the monolith) - publishes a command, does not wait
async acceptOrder(orderId) {
  await this.orderRepository.updateState(orderId, 'ACCEPTED');
  await this.messageQueue.publish('payments', {
    type: 'PROCESS_PAYMENT',
    orderId,
    idempotencyKey: `order-${orderId}-payment`, // stable key for safe retries
    amount: order.totalAmount
  });
  return order; // state becomes PAYMENT_PENDING, resolved asynchronously
}

// Order module - reacts to the result
async onPaymentSucceeded(event) {
  await this.orderRepository.updateState(event.orderId, 'PREPARING');
}

async onPaymentFailed(event) {
  await this.orderRepository.updateState(event.orderId, 'PAYMENT_FAILED');
  await this.notifyCustomer(event.orderId, 'payment_failed');
}
```

```javascript
// Payment Service (separate deployable) - owns the Stripe relationship
async handleProcessPayment(command) {
  const existing = await this.paymentRepository.findByIdempotencyKey(command.idempotencyKey);
  if (existing) return; // already handled - safe to redeliver

  try {
    const result = await this.circuitBreaker.fire(() =>
      this.stripe.charge(command.amount, command.idempotencyKey)
    );
    await this.paymentRepository.save({ ...command, state: 'SUCCEEDED', result });
    await this.messageQueue.publish('orders', { type: 'PAYMENT_SUCCEEDED', orderId: command.orderId });
  } catch (err) {
    await this.paymentRepository.save({ ...command, state: 'FAILED', error: err.message });
    await this.messageQueue.publish('orders', { type: 'PAYMENT_FAILED', orderId: command.orderId });
  }
}
```

### Change 2: Circuit Breaker + Idempotency Around the Payment Provider

**Solves:** Problem 1 (Provider Failures Cascade)

**What:**
- Wrap all Stripe calls in a circuit breaker (e.g. `opossum` in Node, `Polly` in .NET)
- Every payment attempt carries an idempotency key derived from the order ID
- When the circuit is open, queue the attempt for retry with backoff instead of failing immediately

**Why This Solution:**
- Isolates a third-party outage to the Payment service instead of letting it stall order acceptance
- Idempotency key prevents double-charges on retried or redelivered messages (RabbitMQ gives at-least-once delivery)

### Change 3: Align Team Ownership to the New Service Boundary

**Solves:** Problem 3 (Two Teams, One Codebase)

**What:**
- **Team Commerce:** Order, Menu, Restaurant, Delivery (the monolith's core ordering flow)
- **Team Platform:** Identity, Customer, Reporting, and the new standalone Payment service
- Each team owns its deployable(s) end-to-end, including on-call

**Why This Works:**
- Payment extraction gives Team Platform a service they fully own instead of a module they share
- Reduces cross-team PRs; each team's deploy cadence is independent where it matters most (Payment)

### Change 4: Self-Service Restaurant Onboarding

**Solves:** Problem 4 (Manual Onboarding)

**What:**
- Restaurant module gains a self-registration flow with an approval queue for platform staff
- Still inside the existing Restaurant module - no new service needed

**Why Not a New Service:**
- This is a feature gap, not an architectural one; the Restaurant module already owns this data

### Change 5: Scale Notification Workers Horizontally

**Solves:** Problem 5 (Single Worker Bottleneck)

**What:**
- Run multiple instances of the notification worker consuming the same RabbitMQ queue
- RabbitMQ's competing-consumers pattern distributes load automatically; no code change needed beyond deployment config

**Why This Solution:**
- Lowest-effort fix; RabbitMQ already supports this, it's purely an ops change

---

## 4. What Should NOT Change?

| Area | Keep As-Is | Reasoning |
|------|------------|-----------|
| **Order, Menu, Restaurant, Delivery, Identity, Customer, Reporting as monolith modules** | Yes | None of them have Payment's "external, failure-prone dependency" problem; extracting them has no corresponding payoff |
| **Full microservices migration** | No | 12 devs / 2 teams can responsibly own exactly one extracted service, not a dozen |
| **Kubernetes / service mesh** | No | Two deployables (monolith + payment service) don't need orchestration machinery; a simple container deploy pipeline suffices |
| **Saga orchestrator framework** | No | Choreography via RabbitMQ events (as used for notifications since Q2) is sufficient for the one cross-service flow we have |
| **Database sharding** | No | 50 restaurants is still comfortably within a single primary + replica's capacity with proper indexing |
| **Switching payment providers** | No | The problem is resilience around Stripe, not Stripe itself |

---

## 5. Q3 Architecture Summary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MODULAR MONOLITH  (Team Commerce)                        │
│                                                                             │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐                      │
│   │Restaurant│ │  Menu   │  │  Order  │  │Delivery │                      │
│   └─────────┘  └─────────┘  └─────────┘  └─────────┘                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
       │ reads/cache        │ writes               │ publishes/consumes
       ▼                    ▼                       ▼
┌───────────────┐    ┌───────────────┐    ┌─────────────────────────────┐
│     Redis     │    │  PostgreSQL   │    │   RabbitMQ (shared broker)  │
│     Cache     │    │ Primary+Replica│   │  - notifications queue       │
└───────────────┘    └───────────────┘    │  - payments command/event    │
                                           └──────────────┬──────────────┘
                                                            │
                                                            ▼
                              ┌───────────────────────────────────────────────┐
                              │   PAYMENT SERVICE  (Team Platform, own deploy) │
                              │   - Circuit breaker around Stripe             │
                              │   - Idempotency store                         │
                              │   - Own database (payments, attempts)         │
                              └───────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│          MODULAR MONOLITH, continued (Team Platform)                       │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐                                   │
│   │Identity │  │Customer │  │Reporting│                                   │
│   └─────────┘  └─────────┘  └─────────┘                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Problem-to-Solution Mapping

| Problem | Solution | Why This Solution |
|---------|----------|-------------------|
| Payment provider failures cascade | Extract Payment + circuit breaker + idempotency | Isolates third-party failure mode from the rest of the system |
| Coordinated deploys for payment fixes | Independent Payment service deployment | Decouples payment release cycle from monolith release cycle |
| Two teams, one codebase | Team ownership aligned to service boundary | Clear accountability, fewer cross-team PRs |
| Manual restaurant onboarding | Self-service flow in Restaurant module | Feature gap, not architectural - no new service needed |
| Notification worker bottleneck | Horizontal scaling of consumers | RabbitMQ already supports this; zero new infrastructure |

---

## 7. Implementation Priority

| Priority | Change | Effort | Impact | Why This Order |
|----------|--------|--------|--------|----------------|
| 1 | Idempotency keys on payment calls | Low | High | Prevents double-charges immediately, even before full extraction |
| 2 | Circuit breaker around Stripe calls | Low | High | Contains blast radius of provider outages right away |
| 3 | Extract Payment service | High | High | The core Q3 change; needs 1 and 2 done first to extract safely |
| 4 | Realign team ownership | Low | Medium | Process change, cheap once the service boundary exists |
| 5 | Scale notification workers | Low | Medium | Pure ops change, do whenever peak load requires it |
| 6 | Self-service restaurant onboarding | Medium | Medium | Business value, no urgency tied to the other changes |

---

## 8. Decisions Made

### Decision 1: Extract Only Payment, Nothing Else

**Choice:** Payment becomes a standalone service; every other module stays in the monolith.

**Why:**
- Payment is the only module with a genuinely different failure profile (external dependency)
- Extracting modules without that justification would add deployment/ops overhead for no corresponding benefit
- Matches the guiding principle from Q1/Q2: solve the problem you actually have

### Decision 2: Choreography Over a Saga Orchestrator

**Choice:** Order and Payment communicate via RabbitMQ commands/events (choreography), not a dedicated saga orchestration framework.

**Why:**
- Only one cross-service flow exists (place order → process payment → resume order)
- RabbitMQ is already proven in this system since Q2's notification work
- A saga framework would be infrastructure in search of a problem

### Decision 3: Idempotency Key Derived from Order ID

**Choice:** Use a deterministic idempotency key (`order-{id}-payment`) rather than a separately generated UUID per attempt.

**Why:**
- Guarantees retries and redelivered messages for the same order never double-charge
- Simple to reason about; no extra coordination table needed beyond the payment record itself

### Decision 4: Team Boundaries Follow Service Boundaries

**Choice:** Team Platform owns Payment (and Identity/Customer/Reporting); Team Commerce owns the ordering-flow modules.

**Why:**
- Gives the team that already maintained Payment logic a service they fully control end-to-end
- Reduces the ambiguous shared ownership that caused friction in Q2's CODEOWNERS approach

---

## 9. What We're NOT Doing (And Why)

| Option Rejected | Why |
|-----------------|-----|
| Full microservices migration | Only one module has a justification for extraction; the rest gain nothing from it |
| Kubernetes / container orchestration platform | Two deployables don't need orchestration; a simple deploy pipeline per service is enough |
| Saga orchestrator (e.g. Temporal, Camunda) | One cross-service flow doesn't justify a new orchestration framework |
| Database sharding | 50 restaurants is well within a single primary + replica's capacity |
| Switching payment providers | The problem is resilience, not the provider itself |
| Splitting Order and Delivery into separate services | Neither has Payment's external-failure problem; no justification yet |

---

## 10. Comparison: Q2 vs Q3

| Aspect | Q2 | Q3 |
|--------|----|----|
| Deployment | Single modular monolith | Modular monolith + 1 extracted service (Payment) |
| Payment Handling | In-process module call | Async command/event via RabbitMQ to standalone service |
| Payment Resilience | None (direct Stripe call) | Circuit breaker + idempotency key |
| Team Structure | 5 devs, informal module ownership | 12 devs, 2 teams, ownership aligned to deployables |
| Notifications | Single worker | Horizontally scaled workers |
| Restaurant Onboarding | Manual (platform staff) | Self-service with approval queue |
| Infrastructure | Redis, RabbitMQ, read replica | + Payment service, its own database, circuit breaker library |

---

## 11. New Infrastructure

| Component | Purpose | Technology |
|-----------|---------|------------|
| **Payment Service** | Standalone deployable owning the Stripe relationship | Same runtime/language as monolith, separate process |
| **Payment Database** | Payment and payment-attempt records, isolated from the monolith's DB | PostgreSQL (separate instance or schema) |
| **Circuit Breaker** | Contain Stripe outages/latency spikes | e.g. `opossum` (Node) / `Polly` (.NET) |
| **Idempotency Store** | Prevent double-charges on retried/redelivered messages | Table in the Payment database, keyed by idempotency key |
| **Additional RabbitMQ Queues** | `payments` command/event channel between Order and Payment | Same RabbitMQ broker from Q2 |

---

## 12. Guiding Principle

**Solve Q3's problems for Q3's constraints.**

- Extract only what has a genuine failure-mode or ownership justification (Payment) - resist extracting everything just because the team is bigger now
- Reuse Q2's infrastructure (RabbitMQ, Redis, read replica) rather than introducing new platforms
- Team structure should follow the architecture that already makes sense, not the other way around
- Still no Kubernetes, no service mesh, no saga framework - the problems in front of us don't require them

---

## Summary

Q3 is about **extracting one service, deliberately** - not a wholesale move to microservices. The monolith remains the right home for Order, Menu, Restaurant, Delivery, Identity, Customer, and Reporting; only Payment earns its own deployable, because it is the one module with a genuinely different failure profile (a flaky third-party dependency) and genuinely ambiguous ownership between the two teams.

The key changes are:
1. **Payment extraction** - isolate the one component with an external failure mode
2. **Resilience patterns** - circuit breaker and idempotency around the payment provider
3. **Choreography over orchestration** - reuse RabbitMQ rather than adopt a saga framework
4. **Team alignment** - ownership follows the new service boundary, not the reverse
5. **Everything else stays put** - growth in team size and restaurant count doesn't by itself justify further extraction
