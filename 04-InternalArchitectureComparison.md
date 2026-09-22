# Internal Architecture Pattern Comparison

This document provides a detailed comparison of how each architecture pattern would work within our modular monolith, using the food delivery platform components we've already defined.

---

## Table of Contents

1. [Context and Constraints](#1-context-and-constraints)
2. [Pattern 1: Simple & Evolving](#2-pattern-1-simple--evolving)
3. [Pattern 2: Layered / Clean Architecture](#3-pattern-2-layered--clean-architecture)
4. [Pattern 3: Vertical Slice Architecture](#4-pattern-3-vertical-slice-architecture)
5. [Pattern 4: Hexagonal / Ports and Adapters](#5-pattern-4-hexagonal--ports-and-adapters)
6. [Comparison Matrix](#6-comparison-matrix)
7. [Scalability Analysis](#7-scalability-analysis)
8. [Recommendation](#8-recommendation)

---

## 1. Context and Constraints

### What We've Already Decided

- **Deployment**: Modular Monolith (single deployable, single database)
- **Components**: 8 modules (Identity, Customer, Restaurant, Menu, Order, Payment, Delivery, Reporting)
- **Team**: 3 developers
- **Budget**: Limited
- **Goal**: Prove business model, then scale

### What We're Deciding Now

How to structure code **inside** each module. This affects:
- How easy it is to find code
- How testable the code is
- How much ceremony/boilerplate is required
- How easy it is to refactor later
- How new developers onboard

### Example Use Case for Comparison

We'll use **"Place Order"** as the example to show how each pattern structures the code:

```
Customer submits order
    → Validate items exist in menu
    → Calculate total (items + delivery + platform fee)
    → Create order record
    → Notify restaurant
    → Return order confirmation
```

---

## 2. Pattern 1: Simple & Evolving

### Philosophy

Start with the simplest structure that works. Add patterns only when pain emerges. No upfront abstraction.

### Folder Structure

```
src/
├── modules/
│   ├── order/
│   │   ├── OrderController.ts      # HTTP endpoints
│   │   ├── OrderService.ts         # Business logic
│   │   ├── OrderRepository.ts      # Database access
│   │   ├── Order.ts                # Entity/Model
│   │   └── OrderDto.ts             # Request/Response types
│   │
│   ├── payment/
│   │   ├── PaymentController.ts
│   │   ├── PaymentService.ts
│   │   ├── PaymentRepository.ts
│   │   ├── Payment.ts
│   │   └── StripeClient.ts         # Direct external integration
│   │
│   ├── menu/
│   │   ├── MenuController.ts
│   │   ├── MenuService.ts
│   │   ├── MenuRepository.ts
│   │   └── MenuItem.ts
│   │
│   └── ... (other modules)
│
├── shared/
│   ├── database.ts                 # DB connection
│   └── notifications.ts            # Push notification utility
│
└── app.ts                          # Entry point
```

### Code Example: Place Order

**OrderController.ts**
```typescript
// Simple controller - directly calls service
class OrderController {
  constructor(private orderService: OrderService) {}

  async placeOrder(req: Request, res: Response) {
    const { customerId, restaurantId, items, deliveryAddress } = req.body;

    const order = await this.orderService.placeOrder(
      customerId,
      restaurantId,
      items,
      deliveryAddress
    );

    res.status(201).json(order);
  }
}
```

**OrderService.ts**
```typescript
// Service contains business logic
// Directly imports and uses other modules
class OrderService {
  constructor(
    private orderRepo: OrderRepository,
    private menuService: MenuService,        // Direct import from another module
    private restaurantService: RestaurantService,
    private notificationService: NotificationService
  ) {}

  async placeOrder(customerId, restaurantId, items, deliveryAddress) {
    // 1. Validate restaurant is open
    const restaurant = await this.restaurantService.getRestaurant(restaurantId);
    if (!restaurant.isOpen) {
      throw new Error('Restaurant is closed');
    }

    // 2. Validate items exist and are available
    const menuItems = await this.menuService.validateItems(restaurantId, items);

    // 3. Calculate totals
    const itemsTotal = this.calculateItemsTotal(menuItems, items);
    const deliveryFee = restaurant.deliveryFee;
    const platformFee = (itemsTotal + deliveryFee) * 0.10;
    const total = itemsTotal + deliveryFee + platformFee;

    // 4. Create order
    const order = await this.orderRepo.create({
      customerId,
      restaurantId,
      state: 'PENDING_ACCEPTANCE',
      deliveryAddress,
      itemsTotal,
      deliveryFee,
      platformFee,
      totalAmount: total,
      items: menuItems.map(item => ({
        menuItemId: item.id,
        name: item.name,
        price: item.price,
        quantity: items.find(i => i.id === item.id).quantity
      }))
    });

    // 5. Notify restaurant
    await this.notificationService.notifyRestaurant(restaurantId, order);

    return order;
  }
}
```

**OrderRepository.ts**
```typescript
// Repository directly uses database
class OrderRepository {
  async create(orderData) {
    return db.orders.insert(orderData);
  }

  async findById(orderId) {
    return db.orders.findOne({ id: orderId });
  }

  async updateState(orderId, newState) {
    return db.orders.update({ id: orderId }, { state: newState });
  }
}
```

### How Modules Communicate

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SIMPLE & EVOLVING                            │
│                                                                     │
│   OrderService directly imports and calls:                         │
│                                                                     │
│   ┌─────────────┐                                                  │
│   │   Order     │                                                  │
│   │  Service    │                                                  │
│   └──────┬──────┘                                                  │
│          │                                                          │
│          ├──────────▶ MenuService.validateItems()                  │
│          │            (direct import, direct call)                 │
│          │                                                          │
│          ├──────────▶ RestaurantService.getRestaurant()            │
│          │            (direct import, direct call)                 │
│          │                                                          │
│          └──────────▶ NotificationService.notifyRestaurant()       │
│                       (direct import, direct call)                 │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Ceremony Level

| Aspect | Amount |
|--------|--------|
| Files per feature | 3-4 (Controller, Service, Repository, DTO) |
| Interfaces | None required |
| Abstractions | None required |
| Mapping code | Minimal |
| Boilerplate | Low |

### Pros

| Pro | Explanation |
|-----|-------------|
| Fast to build | No upfront abstractions needed |
| Easy to understand | What you see is what you get |
| Low file count | Fewer files to navigate |
| Quick onboarding | New developers can follow the flow easily |
| Flexible | Can evolve in any direction |

### Cons

| Con | Explanation |
|-----|-------------|
| Tight coupling | Modules directly depend on each other's implementations |
| Hard to test in isolation | Need to mock concrete classes |
| Refactoring cost | Changing one service may require changes in many places |
| No enforced boundaries | Easy to accidentally create circular dependencies |
| External dependencies scattered | Stripe client used directly in PaymentService |

### When Things Get Painful

- When you want to swap Stripe for another payment provider
- When you want to test OrderService without a real database
- When multiple developers edit the same service
- When services become 500+ lines

### Refactoring Effort to Scale

| Change | Effort |
|--------|--------|
| Add interfaces for testing | Medium - need to extract interfaces from all services |
| Swap external provider | Medium - find all usages, update each |
| Split into microservices | High - tightly coupled, need to untangle |
| Add caching layer | Medium - modify repository directly |

---

## 3. Pattern 2: Layered / Clean Architecture

### Philosophy

Strict separation of concerns through layers. Dependencies flow inward. Business logic is protected from infrastructure details.

### Folder Structure

```
src/
├── modules/
│   ├── order/
│   │   ├── presentation/           # Layer 1: HTTP/API
│   │   │   ├── OrderController.ts
│   │   │   ├── PlaceOrderRequest.ts
│   │   │   └── OrderResponse.ts
│   │   │
│   │   ├── application/            # Layer 2: Use cases
│   │   │   ├── PlaceOrderUseCase.ts
│   │   │   ├── AcceptOrderUseCase.ts
│   │   │   ├── GetOrderUseCase.ts
│   │   │   └── interfaces/
│   │   │       ├── IOrderRepository.ts
│   │   │       ├── IMenuService.ts
│   │   │       └── INotificationService.ts
│   │   │
│   │   ├── domain/                 # Layer 3: Business logic
│   │   │   ├── Order.ts            # Entity with behavior
│   │   │   ├── OrderItem.ts
│   │   │   ├── OrderState.ts
│   │   │   └── OrderCalculator.ts  # Domain service
│   │   │
│   │   └── infrastructure/         # Layer 4: External
│   │       ├── OrderRepositoryImpl.ts
│   │       └── OrderMapper.ts
│   │
│   ├── payment/
│   │   ├── presentation/
│   │   ├── application/
│   │   ├── domain/
│   │   └── infrastructure/
│   │       └── StripePaymentGateway.ts
│   │
│   └── ... (other modules)
│
└── shared/
    └── infrastructure/
        └── database.ts
```

### Code Example: Place Order

**presentation/OrderController.ts**
```typescript
class OrderController {
  constructor(private placeOrderUseCase: PlaceOrderUseCase) {}

  async placeOrder(req: Request, res: Response) {
    // Map HTTP request to use case input
    const command = new PlaceOrderCommand(
      req.body.customerId,
      req.body.restaurantId,
      req.body.items,
      req.body.deliveryAddress
    );

    const result = await this.placeOrderUseCase.execute(command);

    // Map use case output to HTTP response
    res.status(201).json(OrderResponse.fromOrder(result));
  }
}
```

**application/PlaceOrderUseCase.ts**
```typescript
// Use case depends on INTERFACES, not implementations
class PlaceOrderUseCase {
  constructor(
    private orderRepository: IOrderRepository,      // Interface
    private menuService: IMenuService,              // Interface
    private restaurantService: IRestaurantService,  // Interface
    private notificationService: INotificationService // Interface
  ) {}

  async execute(command: PlaceOrderCommand): Promise<Order> {
    // 1. Validate restaurant
    const restaurant = await this.restaurantService.getRestaurant(command.restaurantId);
    if (!restaurant.isOpen) {
      throw new RestaurantClosedError();
    }

    // 2. Validate menu items
    const menuItems = await this.menuService.validateItems(
      command.restaurantId,
      command.items
    );

    // 3. Create order using DOMAIN logic
    const order = Order.create({
      customerId: command.customerId,
      restaurantId: command.restaurantId,
      deliveryAddress: command.deliveryAddress,
      items: menuItems,
      deliveryFee: restaurant.deliveryFee,
      platformFeePercentage: 0.10
    });

    // 4. Persist
    await this.orderRepository.save(order);

    // 5. Notify
    await this.notificationService.notifyRestaurant(restaurant.id, order);

    return order;
  }
}
```

**domain/Order.ts**
```typescript
// Domain entity contains business logic
class Order {
  private constructor(
    public readonly id: string,
    public readonly customerId: string,
    public readonly restaurantId: string,
    public readonly items: OrderItem[],
    public readonly deliveryAddress: string,
    public readonly itemsTotal: number,
    public readonly deliveryFee: number,
    public readonly platformFee: number,
    public readonly totalAmount: number,
    private _state: OrderState
  ) {}

  // Factory method with business rules
  static create(params: CreateOrderParams): Order {
    const itemsTotal = OrderCalculator.calculateItemsTotal(params.items);
    const platformFee = OrderCalculator.calculatePlatformFee(
      itemsTotal,
      params.deliveryFee,
      params.platformFeePercentage
    );
    const total = itemsTotal + params.deliveryFee + platformFee;

    return new Order(
      generateId(),
      params.customerId,
      params.restaurantId,
      params.items.map(i => OrderItem.create(i)),
      params.deliveryAddress,
      itemsTotal,
      params.deliveryFee,
      platformFee,
      total,
      OrderState.PENDING_ACCEPTANCE
    );
  }

  // State transition with business rules
  accept(): void {
    if (this._state !== OrderState.PENDING_ACCEPTANCE) {
      throw new InvalidStateTransitionError(this._state, OrderState.ACCEPTED);
    }
    this._state = OrderState.ACCEPTED;
  }

  reject(): void {
    if (this._state !== OrderState.PENDING_ACCEPTANCE) {
      throw new InvalidStateTransitionError(this._state, OrderState.REJECTED);
    }
    this._state = OrderState.REJECTED;
  }

  get state(): OrderState {
    return this._state;
  }
}
```

**application/interfaces/IOrderRepository.ts**
```typescript
// Interface - no implementation details
interface IOrderRepository {
  save(order: Order): Promise<void>;
  findById(id: string): Promise<Order | null>;
  findByCustomer(customerId: string): Promise<Order[]>;
}
```

**infrastructure/OrderRepositoryImpl.ts**
```typescript
// Implementation of interface
class OrderRepositoryImpl implements IOrderRepository {
  async save(order: Order): Promise<void> {
    const data = OrderMapper.toDatabase(order);
    await db.orders.upsert(data);
  }

  async findById(id: string): Promise<Order | null> {
    const data = await db.orders.findOne({ id });
    return data ? OrderMapper.toDomain(data) : null;
  }
}
```

### How Modules Communicate

```
┌─────────────────────────────────────────────────────────────────────┐
│                      LAYERED / CLEAN                                │
│                                                                     │
│   Use cases depend on INTERFACES, implementations injected         │
│                                                                     │
│   ┌─────────────────┐                                              │
│   │ PlaceOrderUse   │                                              │
│   │     Case        │                                              │
│   └────────┬────────┘                                              │
│            │                                                        │
│            │  depends on interfaces                                │
│            │                                                        │
│   ┌────────┼────────────────┬─────────────────┐                    │
│   │        │                │                 │                    │
│   ▼        ▼                ▼                 ▼                    │
│ IOrder   IMenu           IRestaurant      INotification            │
│ Repo     Service         Service          Service                  │
│   │        │                │                 │                    │
│   │        │                │                 │                    │
│   ▼        ▼                ▼                 ▼                    │
│ OrderRepo MenuService   Restaurant      Notification               │
│ Impl      Impl          ServiceImpl     ServiceImpl                │
│                                                                     │
│   Implementations injected at startup (DI container)               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Ceremony Level

| Aspect | Amount |
|--------|--------|
| Files per feature | 8-12 (Controller, Request, Response, UseCase, Interfaces, Domain, Repository, Mapper) |
| Interfaces | Required for all dependencies |
| Abstractions | High - everything goes through interfaces |
| Mapping code | Significant - DTO ↔ Domain ↔ Database |
| Boilerplate | Medium-High |

### Pros

| Pro | Explanation |
|-----|-------------|
| Highly testable | Mock interfaces, test each layer |
| Clear boundaries | Each layer has specific responsibility |
| Business logic protected | Domain doesn't know about HTTP or database |
| Easy to swap implementations | Just create new implementation of interface |
| Scales well | Clear structure as codebase grows |

### Cons

| Con | Explanation |
|-----|-------------|
| More files | 3x more files than simple approach |
| Mapping overhead | Need mappers between layers |
| Indirection | Have to follow interface → implementation |
| Slower initial development | More structure to set up |
| Can feel bureaucratic | Simple CRUD requires many files |

### When It Pays Off

- When you need to swap external providers (payment, notifications)
- When you have complex business rules
- When you want comprehensive unit tests
- When team grows beyond 3 developers

### Refactoring Effort to Scale

| Change | Effort |
|--------|--------|
| Add new implementation | Low - just implement interface |
| Swap external provider | Low - create new adapter, no core changes |
| Split into microservices | Medium - boundaries already defined by interfaces |
| Add caching layer | Low - decorator pattern on repository interface |

---

## 4. Pattern 3: Vertical Slice Architecture

### Philosophy

Organize by feature, not by layer. Each feature is self-contained with everything it needs. Minimize coupling between features.

### Folder Structure

```
src/
├── modules/
│   ├── order/
│   │   ├── features/
│   │   │   ├── place-order/
│   │   │   │   ├── PlaceOrderHandler.ts      # Contains ALL logic
│   │   │   │   ├── PlaceOrderRequest.ts
│   │   │   │   ├── PlaceOrderResponse.ts
│   │   │   │   ├── PlaceOrderValidator.ts
│   │   │   │   └── place-order.test.ts
│   │   │   │
│   │   │   ├── accept-order/
│   │   │   │   ├── AcceptOrderHandler.ts
│   │   │   │   ├── AcceptOrderRequest.ts
│   │   │   │   └── accept-order.test.ts
│   │   │   │
│   │   │   ├── get-order/
│   │   │   │   ├── GetOrderHandler.ts
│   │   │   │   ├── GetOrderResponse.ts
│   │   │   │   └── get-order.test.ts
│   │   │   │
│   │   │   └── get-order-history/
│   │   │       ├── GetOrderHistoryHandler.ts
│   │   │       └── GetOrderHistoryResponse.ts
│   │   │
│   │   └── shared/                    # Shared within module only
│   │       ├── Order.ts
│   │       └── OrderState.ts
│   │
│   ├── payment/
│   │   ├── features/
│   │   │   ├── process-payment/
│   │   │   └── get-payment-status/
│   │   └── shared/
│   │
│   └── ... (other modules)
│
└── shared/                            # Cross-module shared code
    ├── database.ts
    └── notifications.ts
```

### Code Example: Place Order

**features/place-order/PlaceOrderHandler.ts**
```typescript
// EVERYTHING for this feature is in this file or folder
// No external service dependencies - handler does it all

class PlaceOrderHandler {
  async handle(request: PlaceOrderRequest): Promise<PlaceOrderResponse> {
    // 1. Validate request
    PlaceOrderValidator.validate(request);

    // 2. Get restaurant (direct database query - no service layer)
    const restaurant = await db.restaurants.findOne({
      id: request.restaurantId
    });

    if (!restaurant || !restaurant.isOpen) {
      throw new RestaurantNotAvailableError();
    }

    // 3. Validate and get menu items (direct query)
    const menuItems = await db.menuItems.find({
      id: { $in: request.items.map(i => i.itemId) },
      restaurantId: request.restaurantId,
      isAvailable: true
    });

    if (menuItems.length !== request.items.length) {
      throw new InvalidMenuItemsError();
    }

    // 4. Calculate totals (inline - specific to this feature)
    const itemsTotal = request.items.reduce((sum, item) => {
      const menuItem = menuItems.find(m => m.id === item.itemId);
      return sum + (menuItem.price * item.quantity);
    }, 0);

    const deliveryFee = restaurant.deliveryFee;
    const platformFee = (itemsTotal + deliveryFee) * 0.10;
    const total = itemsTotal + deliveryFee + platformFee;

    // 5. Create order (direct insert)
    const order = await db.orders.insert({
      id: generateId(),
      customerId: request.customerId,
      restaurantId: request.restaurantId,
      state: 'PENDING_ACCEPTANCE',
      deliveryAddress: request.deliveryAddress,
      itemsTotal,
      deliveryFee,
      platformFee,
      totalAmount: total,
      items: request.items.map(item => ({
        menuItemId: item.itemId,
        name: menuItems.find(m => m.id === item.itemId).name,
        price: menuItems.find(m => m.id === item.itemId).price,
        quantity: item.quantity
      })),
      createdAt: new Date()
    });

    // 6. Send notification (direct call)
    await pushNotification.send(restaurant.userId, {
      type: 'NEW_ORDER',
      orderId: order.id
    });

    // 7. Return response
    return PlaceOrderResponse.from(order);
  }
}
```

**features/place-order/PlaceOrderRequest.ts**
```typescript
interface PlaceOrderRequest {
  customerId: string;
  restaurantId: string;
  deliveryAddress: string;
  items: Array<{
    itemId: string;
    quantity: number;
  }>;
}
```

**features/place-order/PlaceOrderValidator.ts**
```typescript
class PlaceOrderValidator {
  static validate(request: PlaceOrderRequest): void {
    if (!request.customerId) throw new ValidationError('customerId required');
    if (!request.restaurantId) throw new ValidationError('restaurantId required');
    if (!request.items?.length) throw new ValidationError('items required');
    // ... more validation
  }
}
```

### How Modules Communicate

```
┌─────────────────────────────────────────────────────────────────────┐
│                      VERTICAL SLICES                                │
│                                                                     │
│   Each handler is SELF-CONTAINED                                   │
│   Communication is through database or direct calls                │
│                                                                     │
│   ┌─────────────────┐    ┌─────────────────┐                       │
│   │  PlaceOrder     │    │  AcceptOrder    │                       │
│   │   Handler       │    │   Handler       │                       │
│   │                 │    │                 │                       │
│   │ - validates     │    │ - validates     │                       │
│   │ - queries db    │    │ - queries db    │                       │
│   │ - calculates    │    │ - updates state │                       │
│   │ - inserts       │    │ - notifies      │                       │
│   │ - notifies      │    │                 │                       │
│   └────────┬────────┘    └────────┬────────┘                       │
│            │                      │                                 │
│            │                      │                                 │
│            ▼                      ▼                                 │
│   ┌─────────────────────────────────────────────────┐              │
│   │                   DATABASE                       │              │
│   │   (shared state - source of truth)              │              │
│   └─────────────────────────────────────────────────┘              │
│                                                                     │
│   Cross-module communication:                                      │
│   - PlaceOrderHandler needs menu items → queries menuItems table   │
│   - No MenuService dependency, just direct query                   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Ceremony Level

| Aspect | Amount |
|--------|--------|
| Files per feature | 3-5 (Handler, Request, Response, Validator, Test) |
| Interfaces | Minimal - only where truly needed |
| Abstractions | Low |
| Mapping code | Minimal - request/response only |
| Boilerplate | Low |

### Pros

| Pro | Explanation |
|-----|-------------|
| Easy to find code | All code for a feature in one place |
| Independent features | Change one feature without touching others |
| Can delete features cleanly | Remove the folder, done |
| Parallel development | Developers can work on different features |
| No over-abstraction | Each feature has exactly what it needs |

### Cons

| Con | Explanation |
|-----|-------------|
| Code duplication | Similar logic may appear in multiple handlers |
| Cross-cutting concerns | Harder to apply changes across features |
| No shared business rules | Order state logic duplicated across handlers |
| Database coupling | Handlers directly query database |
| Testing external deps | Harder to mock database in tests |

### When It Pays Off

- When features are truly independent
- When team can work on separate features
- When you want to move fast per feature
- When business rules are simple

### Refactoring Effort to Scale

| Change | Effort |
|--------|--------|
| Add new feature | Low - create new folder, implement |
| Change shared behavior | High - need to update each handler |
| Swap external provider | High - provider used directly in handlers |
| Split into microservices | Medium - features independent, but shared database |
| Extract shared logic | Medium - identify duplicates, create shared module |

---

## 5. Pattern 4: Hexagonal / Ports and Adapters

### Philosophy

Core domain at the center, completely isolated. "Ports" define how the domain communicates. "Adapters" implement those ports for specific technologies.

### Folder Structure

```
src/
├── modules/
│   ├── order/
│   │   ├── core/                      # THE HEXAGON (pure business logic)
│   │   │   ├── domain/
│   │   │   │   ├── Order.ts
│   │   │   │   ├── OrderItem.ts
│   │   │   │   ├── OrderState.ts
│   │   │   │   └── OrderCalculator.ts
│   │   │   │
│   │   │   ├── ports/
│   │   │   │   ├── incoming/          # How outside calls IN
│   │   │   │   │   ├── PlaceOrderPort.ts
│   │   │   │   │   ├── AcceptOrderPort.ts
│   │   │   │   │   └── GetOrderPort.ts
│   │   │   │   │
│   │   │   │   └── outgoing/          # How core calls OUT
│   │   │   │       ├── OrderRepositoryPort.ts
│   │   │   │       ├── MenuQueryPort.ts
│   │   │   │       ├── RestaurantQueryPort.ts
│   │   │   │       └── NotificationPort.ts
│   │   │   │
│   │   │   └── services/              # Domain services
│   │   │       └── OrderService.ts    # Implements incoming ports
│   │   │
│   │   └── adapters/                  # OUTSIDE THE HEXAGON
│   │       ├── incoming/              # Driving adapters
│   │       │   ├── rest/
│   │       │   │   └── OrderController.ts
│   │       │   └── graphql/           # Could add later
│   │       │       └── OrderResolver.ts
│   │       │
│   │       └── outgoing/              # Driven adapters
│   │           ├── persistence/
│   │           │   └── PostgresOrderRepository.ts
│   │           ├── menu/
│   │           │   └── MenuModuleAdapter.ts
│   │           └── notifications/
│   │               └── FirebaseNotificationAdapter.ts
│   │
│   ├── payment/
│   │   ├── core/
│   │   │   ├── domain/
│   │   │   ├── ports/
│   │   │   └── services/
│   │   └── adapters/
│   │       └── outgoing/
│   │           └── StripePaymentAdapter.ts
│   │
│   └── ... (other modules)
│
└── config/
    └── dependency-injection.ts        # Wires adapters to ports
```

### Code Example: Place Order

**core/ports/incoming/PlaceOrderPort.ts**
```typescript
// Incoming port - defines what operations the core supports
interface PlaceOrderPort {
  placeOrder(command: PlaceOrderCommand): Promise<Order>;
}

interface PlaceOrderCommand {
  customerId: string;
  restaurantId: string;
  items: Array<{ itemId: string; quantity: number }>;
  deliveryAddress: string;
}
```

**core/ports/outgoing/OrderRepositoryPort.ts**
```typescript
// Outgoing port - defines what the core needs from outside
interface OrderRepositoryPort {
  save(order: Order): Promise<void>;
  findById(id: string): Promise<Order | null>;
  findByCustomer(customerId: string): Promise<Order[]>;
}
```

**core/ports/outgoing/MenuQueryPort.ts**
```typescript
// Outgoing port for querying menu (different module)
interface MenuQueryPort {
  getAvailableItems(restaurantId: string, itemIds: string[]): Promise<MenuItem[]>;
}
```

**core/services/OrderService.ts**
```typescript
// Domain service implements incoming ports, uses outgoing ports
class OrderService implements PlaceOrderPort, AcceptOrderPort {
  constructor(
    private orderRepository: OrderRepositoryPort,
    private menuQuery: MenuQueryPort,
    private restaurantQuery: RestaurantQueryPort,
    private notifications: NotificationPort
  ) {}

  async placeOrder(command: PlaceOrderCommand): Promise<Order> {
    // 1. Query restaurant through port
    const restaurant = await this.restaurantQuery.getRestaurant(command.restaurantId);
    if (!restaurant.isOpen) {
      throw new RestaurantClosedError();
    }

    // 2. Query menu items through port
    const menuItems = await this.menuQuery.getAvailableItems(
      command.restaurantId,
      command.items.map(i => i.itemId)
    );

    // 3. Create order using DOMAIN (pure business logic)
    const order = Order.create({
      customerId: command.customerId,
      restaurantId: command.restaurantId,
      items: command.items.map(i => {
        const menuItem = menuItems.find(m => m.id === i.itemId);
        return OrderItem.create(menuItem, i.quantity);
      }),
      deliveryAddress: command.deliveryAddress,
      deliveryFee: restaurant.deliveryFee,
      platformFeePercentage: 0.10
    });

    // 4. Persist through port
    await this.orderRepository.save(order);

    // 5. Notify through port
    await this.notifications.notifyNewOrder(restaurant.id, order);

    return order;
  }

  async acceptOrder(orderId: string): Promise<Order> {
    const order = await this.orderRepository.findById(orderId);
    if (!order) throw new OrderNotFoundError(orderId);

    order.accept(); // Domain method with business rules

    await this.orderRepository.save(order);
    return order;
  }
}
```

**adapters/incoming/rest/OrderController.ts**
```typescript
// REST adapter - translates HTTP to port calls
class OrderController {
  constructor(private placeOrderPort: PlaceOrderPort) {}

  async placeOrder(req: Request, res: Response) {
    const command: PlaceOrderCommand = {
      customerId: req.body.customerId,
      restaurantId: req.body.restaurantId,
      items: req.body.items,
      deliveryAddress: req.body.deliveryAddress
    };

    const order = await this.placeOrderPort.placeOrder(command);

    res.status(201).json(this.toResponse(order));
  }

  private toResponse(order: Order) {
    return {
      id: order.id,
      state: order.state,
      total: order.totalAmount
    };
  }
}
```

**adapters/outgoing/persistence/PostgresOrderRepository.ts**
```typescript
// Persistence adapter - implements repository port
class PostgresOrderRepository implements OrderRepositoryPort {
  async save(order: Order): Promise<void> {
    const data = this.toDatabase(order);
    await db.orders.upsert(data);
  }

  async findById(id: string): Promise<Order | null> {
    const data = await db.orders.findOne({ id });
    return data ? this.toDomain(data) : null;
  }

  private toDatabase(order: Order): DbOrder {
    // Map domain to database schema
  }

  private toDomain(data: DbOrder): Order {
    // Map database to domain
  }
}
```

**adapters/outgoing/menu/MenuModuleAdapter.ts**
```typescript
// Adapter to call another module through its port
class MenuModuleAdapter implements MenuQueryPort {
  constructor(private menuModule: MenuQueryPort) {}

  async getAvailableItems(restaurantId: string, itemIds: string[]): Promise<MenuItem[]> {
    return this.menuModule.getAvailableItems(restaurantId, itemIds);
  }
}
```

### How Modules Communicate

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                        HEXAGONAL / PORTS AND ADAPTERS                           │
│                                                                                 │
│                                                                                 │
│    ┌───────────────────────────────────────────────────────────────────────┐   │
│    │                         DRIVING ADAPTERS                               │   │
│    │                    (incoming - call INTO core)                         │   │
│    │                                                                        │   │
│    │   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐            │   │
│    │   │ REST API     │    │  GraphQL     │    │   CLI        │            │   │
│    │   │ Controller   │    │  Resolver    │    │  Command     │            │   │
│    │   └──────┬───────┘    └──────┬───────┘    └──────┬───────┘            │   │
│    │          │                   │                   │                     │   │
│    └──────────┼───────────────────┼───────────────────┼─────────────────────┘   │
│               │                   │                   │                         │
│               ▼                   ▼                   ▼                         │
│         ╔═══════════════════════════════════════════════════════╗              │
│         ║               INCOMING PORTS                          ║              │
│         ║  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ ║              │
│         ║  │PlaceOrderPort│  │AcceptOrder   │  │GetOrderPort  │ ║              │
│         ║  │              │  │Port          │  │              │ ║              │
│         ║  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘ ║              │
│         ║         │                 │                 │         ║              │
│         ║         ▼                 ▼                 ▼         ║              │
│         ║  ┌────────────────────────────────────────────────┐   ║              │
│         ║  │                                                │   ║              │
│         ║  │              DOMAIN / CORE                     │   ║              │
│         ║  │                                                │   ║              │
│         ║  │   Order.ts   OrderService.ts   OrderState.ts   │   ║              │
│         ║  │                                                │   ║              │
│         ║  │         (Pure business logic)                  │   ║              │
│         ║  │                                                │   ║              │
│         ║  └────────────────────────────────────────────────┘   ║              │
│         ║         │                 │                 │         ║              │
│         ║         ▼                 ▼                 ▼         ║              │
│         ║  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ ║              │
│         ║  │OrderRepo     │  │MenuQuery     │  │Notification  │ ║              │
│         ║  │Port          │  │Port          │  │Port          │ ║              │
│         ║  └──────────────┘  └──────────────┘  └──────────────┘ ║              │
│         ║               OUTGOING PORTS                          ║              │
│         ╚═══════════════════════════════════════════════════════╝              │
│               │                   │                   │                         │
│               ▼                   ▼                   ▼                         │
│    ┌──────────┼───────────────────┼───────────────────┼─────────────────────┐   │
│    │          │                   │                   │                     │   │
│    │   ┌──────▼───────┐    ┌──────▼───────┐    ┌──────▼───────┐            │   │
│    │   │  Postgres    │    │  Menu Module │    │  Firebase    │            │   │
│    │   │  Repository  │    │  Adapter     │    │  Adapter     │            │   │
│    │   └──────────────┘    └──────────────┘    └──────────────┘            │   │
│    │                                                                        │   │
│    │                         DRIVEN ADAPTERS                                │   │
│    │                    (outgoing - called BY core)                         │   │
│    └────────────────────────────────────────────────────────────────────────┘   │
│                                                                                 │
└─────────────────────────────────────────────────────────────────────────────────┘

Cross-module communication:
- OrderService needs menu data
- OrderService depends on MenuQueryPort (interface)
- MenuModuleAdapter implements MenuQueryPort
- MenuModuleAdapter calls Menu module's public port
- Modules are decoupled through ports
```

### Ceremony Level

| Aspect | Amount |
|--------|--------|
| Files per feature | 10-15 (Ports, Domain, Service, Adapters, Mappers) |
| Interfaces | High - ports for everything |
| Abstractions | Very high - hexagon isolated |
| Mapping code | Significant - each adapter maps |
| Boilerplate | High |

### Pros

| Pro | Explanation |
|-----|-------------|
| Maximum testability | Core is pure, no dependencies to mock |
| Technology agnostic | Swap database, API style, notification provider easily |
| Clear architecture | Ports define exact boundaries |
| Multiple entry points | REST, GraphQL, CLI can all use same core |
| Excellent for complex domains | Business logic fully isolated |

### Cons

| Con | Explanation |
|-----|-------------|
| High ceremony | Many files, many interfaces |
| Steep learning curve | Team needs to understand ports/adapters concept |
| Overkill for CRUD | Simple features still need full structure |
| Mapping overhead | Every adapter needs mappers |
| Initial development slower | More structure to set up |

### When It Pays Off

- When you have multiple interfaces (REST + GraphQL + CLI)
- When you need to swap technologies (database, providers)
- When domain logic is complex and needs protection
- When you want to test business logic without any infrastructure

### Refactoring Effort to Scale

| Change | Effort |
|--------|--------|
| Add new interface (GraphQL) | Low - create new driving adapter |
| Swap external provider | Very Low - create new driven adapter |
| Split into microservices | Low - boundaries already defined by ports |
| Add caching layer | Very Low - decorator on port |
| Test core logic | Very Low - core has no external dependencies |

---

## 6. Comparison Matrix

### Files Per Feature

| Pattern | Files | Example for "Place Order" |
|---------|-------|---------------------------|
| Simple & Evolving | 3-4 | Controller, Service, Repository, DTO |
| Layered/Clean | 8-12 | Controller, Request, Response, UseCase, 3 Interfaces, Domain, Repository, Mapper |
| Vertical Slices | 3-5 | Handler, Request, Response, Validator, Test |
| Hexagonal | 10-15 | 2 Ports, Domain, Service, Controller Adapter, Repository Adapter, Menu Adapter, Notification Adapter, Mappers |

### Coupling and Testability

| Pattern | Module Coupling | Testability | Mock Complexity |
|---------|-----------------|-------------|-----------------|
| Simple & Evolving | High (direct imports) | Medium | Mock concrete classes |
| Layered/Clean | Low (interfaces) | High | Mock interfaces |
| Vertical Slices | Medium (shared DB) | Medium | Mock database |
| Hexagonal | Very Low (ports) | Very High | Mock ports (simple interfaces) |

### Development Speed

| Pattern | Initial Speed | Speed at 10 Features | Speed at 50 Features |
|---------|---------------|----------------------|----------------------|
| Simple & Evolving | Fast | Medium | Slow (tangled) |
| Layered/Clean | Medium | Medium | Medium (consistent) |
| Vertical Slices | Fast | Fast | Medium (some duplication) |
| Hexagonal | Slow | Medium | Fast (clean boundaries) |

### Scalability Fit

| Pattern | Works with Modular Monolith? | Ease of Migration to Microservices |
|---------|------------------------------|-----------------------------------|
| Simple & Evolving | Yes, but can tangle | Hard - need to untangle dependencies |
| Layered/Clean | Yes, very well | Medium - interfaces help but still work needed |
| Vertical Slices | Yes | Medium - features independent but share DB |
| Hexagonal | Yes, excellent | Easy - ports already define service boundaries |

### Team Size Suitability

| Pattern | 3 Developers | 5-7 Developers | 10+ Developers |
|---------|--------------|----------------|----------------|
| Simple & Evolving | Good | Risky | Poor |
| Layered/Clean | Good | Good | Good |
| Vertical Slices | Good | Good | Good |
| Hexagonal | Overkill? | Good | Excellent |

---

## 7. Scalability Analysis

### Question 2 Scenario (15 restaurants, 5 developers)

| Problem | Simple | Layered | Vertical | Hexagonal |
|---------|--------|---------|----------|-----------|
| Reporting queries slow | Modify repos directly | Add caching interface impl | Add caching to handlers | Add caching adapter |
| Notifications slow | Add queue directly | Add async interface impl | Add queue to handlers | Add async notification adapter |
| More devs in same code | Will conflict | Clear layer boundaries help | Features are separate | Ports define clear contracts |
| Need background jobs | Add directly to services | Add job processor interface | Add job handlers | Add job-triggered ports |

**Refactoring Effort for Question 2:**

| Pattern | Effort to Add Caching | Effort to Add Queues | Effort to Add Background Jobs |
|---------|----------------------|---------------------|-------------------------------|
| Simple & Evolving | Medium (find all DB calls) | Medium (find all notifications) | Medium (add job scheduling) |
| Layered/Clean | Low (implement cache interface) | Low (implement async interface) | Low (add job use cases) |
| Vertical Slices | Medium (update each handler) | Medium (update each handler) | Low (add job handlers) |
| Hexagonal | Very Low (cache adapter) | Very Low (async adapter) | Very Low (job-triggered adapter) |

### Question 3 Scenario (50 restaurants, 12 developers, 2 teams)

| Challenge | Simple | Layered | Vertical | Hexagonal |
|-----------|--------|---------|----------|-----------|
| Extract service | Hard - coupled | Medium - interfaces help | Medium - independent features | Easy - ports define boundaries |
| Team ownership | Hard - shared code | Medium - layer ownership | Good - feature ownership | Good - module ownership |
| Independent deployment | Not possible | Difficult | Difficult | Easier with ports |
| Payment provider failure | Logic scattered | Isolated in layer | In each handler | Completely isolated adapter |

---

## 8. Recommendation

### For Your Specific Constraints

**Current State:**
- 3 developers
- Limited budget
- MVP / prove business model
- Need to move fast

**Expected Growth:**
- Question 2: 5 developers, 15 restaurants
- Question 3: 12 developers, 50 restaurants, 2 teams

### Analysis

| Pattern | MVP Fit | Growth Fit | Overall |
|---------|---------|------------|---------|
| Simple & Evolving | Excellent | Poor | Not recommended - will require rewrite |
| Layered/Clean | Good | Good | Safe choice, balanced |
| Vertical Slices | Excellent | Medium | Good if features stay independent |
| Hexagonal | Poor (too slow) | Excellent | Overkill for MVP |

### Recommended Approach: Layered/Clean with Pragmatism

**Why:**
1. **Familiar to most developers** - layers are widely understood
2. **Clear structure** - helps 3 devs stay organized
3. **Testable** - interfaces enable mocking
4. **Scales well** - structure holds as team grows
5. **Not too much ceremony** - less than full Hexagonal
6. **External deps isolated** - payment provider wrapped in interface

**Pragmatic Adjustments:**
1. Don't create interfaces for everything upfront - add when needed for testing or swapping
2. Keep mapping simple - not every layer needs its own model initially
3. Start with one implementation - add abstractions when you have a second use case
4. Allow reading across modules - strict write ownership, flexible reads

### Suggested Structure (Pragmatic Layered)

```
src/
├── modules/
│   ├── order/
│   │   ├── OrderController.ts         # Presentation
│   │   ├── OrderService.ts            # Application/Use cases
│   │   ├── OrderRepository.ts         # Infrastructure (concrete)
│   │   ├── IOrderRepository.ts        # Interface (add when needed for testing)
│   │   ├── Order.ts                   # Domain entity
│   │   └── OrderDto.ts                # Request/Response
│   │
│   ├── payment/
│   │   ├── PaymentController.ts
│   │   ├── PaymentService.ts
│   │   ├── IPaymentGateway.ts         # Interface for external dep (add from start)
│   │   └── StripePaymentGateway.ts    # Implementation
│   │
│   └── ... (other modules)
│
└── shared/
    └── ...
```

**Rules:**
1. External dependencies get interfaces from the start (Payment, Notifications)
2. Internal repositories start concrete, add interface when you need to mock
3. Services contain business logic, don't let controllers do business logic
4. Modules can read each other's data, only write to own data
5. When a service gets too big, extract a use case class

### Evolution Path

**MVP (Now):**
- Pragmatic layered structure
- Interfaces only for external dependencies
- Simple, get to market

**Question 2 (Growth):**
- Add interfaces for repositories (testing)
- Extract use cases from large services
- Add caching at repository level
- Add async processing for notifications

**Question 3 (Scale):**
- Consider extracting payment to separate service
- Move to full hexagonal for complex modules
- Keep simple modules as-is

---

## 9. Why Pragmatic Layered/Clean - The Argument

### The Question

> "Why do we pick Pragmatic Layered/Clean Architecture over the other options when building a modular monolith?"

### The Defense

#### Against Simple & Evolving

| Argument | Counter | Our Response |
|----------|---------|--------------|
| "It's faster to start" | True | But we're not building a prototype. We're building a business. The 20% extra time now saves 200% later. |
| "We can refactor later" | Risky | Refactoring tightly coupled code while the business is running is painful. Customers don't wait. |
| "YAGNI - we don't need interfaces" | Partially true | We need interfaces for external dependencies (payment) from day 1. The rest can evolve. |

**Why NOT Simple & Evolving:**
- Our payment provider WILL need to be abstracted (Question 3 proves this)
- Without structure, 3 developers will step on each other's toes
- Technical debt compounds faster than you think
- The "refactor later" rarely happens - features keep coming

#### Against Full Vertical Slices

| Argument | Counter | Our Response |
|----------|---------|--------------|
| "Each feature is independent" | True | But we have shared domain concepts (Order, Payment) that cross features |
| "Easy to delete features" | True | But we rarely delete features in a food delivery app |
| "Parallel development" | True | But with 3 developers, we're not parallelizing much yet |

**Why NOT Full Vertical Slices:**
- Order state logic needs to be shared (accept order, reject order, deliver order all need same rules)
- Duplication is acceptable in some cases, but not for core domain rules
- Works better when features are truly independent (which ours aren't)

#### Against Full Hexagonal

| Argument | Counter | Our Response |
|----------|---------|--------------|
| "Maximum flexibility" | True | But we don't need maximum flexibility. We need to ship. |
| "Best for scaling" | True | But we're not at scale yet. We might never reach scale. |
| "Pure domain" | True | But our domain isn't that complex. It's food ordering. |

**Why NOT Full Hexagonal:**
- Too much ceremony for 3 developers
- 15 files for a simple "place order" feature is excessive
- The learning curve slows down initial development
- We can evolve TO hexagonal later IF needed

### Why Pragmatic Layered/Clean IS the Right Choice

#### 1. It Matches Our Team Size

```
3 Developers + Clear Layers = Each person knows where code goes
```

- Developer A works on Controllers (presentation)
- Developer B works on Services (business logic)
- Developer C works on Repositories (data access)

No confusion. No stepping on toes.

#### 2. It Solves the Problems We Actually Have

| Problem We Have | How Layered Solves It |
|-----------------|----------------------|
| Need to test business logic | Services have no framework dependencies |
| Need to swap payment provider | IPaymentGateway interface from day 1 |
| Need clear code organization | Layers provide natural structure |
| Need to onboard new developers | Layered is widely understood pattern |

#### 3. It Doesn't Solve Problems We Don't Have

| Problem We DON'T Have | What We're NOT Doing |
|-----------------------|---------------------|
| Multiple API formats (REST + GraphQL) | No need for full port/adapter abstraction |
| 10 teams working independently | No need for isolated bounded contexts |
| Complex domain with many business rules | No need for full DDD patterns |

#### 4. It's the "Boring Technology" Choice

> "Boring technology is good technology." - Dan McKinley

Layered architecture is:
- Taught in university courses
- Used by millions of applications
- Understood by most developers
- Has decades of proven success
- Easy to find help online

#### 5. It Grows With Us

```
Today (MVP)         → Question 2        → Question 3
Pragmatic Layered   → Add interfaces    → Extract services
                    → Add caching       → Move to hexagonal
                    → Add async         → for complex modules
```

We're not locked in. We can add more structure when we need it.

### Is It Too Much Ceremony?

**The honest answer: It depends on perspective.**

| Perspective | Is It Too Much? |
|-------------|-----------------|
| Compared to Simple & Evolving | Yes, slightly more files |
| Compared to Hexagonal | No, significantly less |
| Compared to no structure | Yes, but that's the point |
| For proving business model | No, structure helps us move faster with confidence |

**The ceremony we're adding:**
- Separate controller and service files (not controversial)
- Interface for payment gateway (necessary)
- Clear module boundaries (helpful)

**The ceremony we're NOT adding:**
- Interfaces for every repository (add later if needed)
- Separate request/response DTOs for everything (keep simple)
- Domain events (overkill for MVP)
- Full use case classes (services are fine for now)

### What If the Business Never Grows?

> "Are we over-engineering if the business stays at 1 restaurant forever?"

**Honest assessment:**

If the business stays at 1 restaurant with a few orders per day:
- The architecture "overhead" is minimal (a few extra files)
- The code is still maintainable
- No developer will complain about clear structure
- The "wasted" effort is maybe 2-3 days total

**Compared to the alternative:**

If we use Simple & Evolving and the business DOES grow:
- Refactoring takes weeks, not days
- Risk of bugs during refactor
- Developers frustrated with tangled code
- Possible need to rewrite entirely

**The asymmetry:**

```
Scenario A: We use Layered, business doesn't grow
Result: Wasted ~2-3 days of structure. No other harm.

Scenario B: We use Simple, business grows
Result: Weeks of painful refactoring. Risk to business. Developer frustration.
```

The downside of being "wrong" with Layered is much smaller than the downside of being "wrong" with Simple.

### The Final Argument

**We choose Pragmatic Layered/Clean because:**

1. **It's not the fastest approach** - but "fast" means nothing if we have to rewrite later
2. **It's not the most flexible approach** - but we don't need maximum flexibility
3. **It's the right amount of structure** - enough to keep us organized, not so much that we're drowning in ceremony
4. **It's a one-way door we can walk back through** - we can always add more structure, but removing structure is painful
5. **It's defensible to any technical reviewer** - no one will criticize clear layers and dependency injection

---

## 10. Domain-Driven Design as a Modeling Approach

### DDD Is Not a Code Structure - It's a Modeling Philosophy

Domain-Driven Design (DDD) is often listed alongside Clean Architecture and Hexagonal, but it's fundamentally different:

| Aspect | Clean/Layered/Hexagonal | Domain-Driven Design |
|--------|-------------------------|----------------------|
| What it is | Code organization pattern | Modeling approach/philosophy |
| Focus | Where code goes | How to think about the domain |
| Answers | "Which folder?" | "What are the business concepts?" |
| Can use together? | N/A | Yes - DDD + Clean Architecture |

**DDD complements Clean Architecture** - it tells us HOW to model the domain layer that Clean Architecture protects.

### DDD Concepts Applied to Our Food Delivery Platform

#### Concept 1: Ubiquitous Language

**What it is:** The team (developers + business) uses the same terms everywhere - in code, conversations, and documentation.

**Applied to our system:**

| Term | Meaning | Used Consistently In |
|------|---------|---------------------|
| Order | A customer's request for food | Code, database, UI, discussions |
| Delivery | The act of transporting order to customer | Not "shipment" or "transport" |
| Driver | Person who delivers | Not "courier" or "delivery person" |
| Menu Item | A food item available for order | Not "product" or "SKU" |
| Restaurant | Food preparation location | Not "vendor" or "merchant" |

**In our code:**
```typescript
// GOOD - Uses ubiquitous language
class Order {
  accept(): void { }
  reject(): void { }
  markAsDelivered(): void { }
}

// BAD - Technical terms, not business terms
class OrderEntity {
  updateStatus(status: number): void { }
  setFlag(flag: boolean): void { }
}
```

#### Concept 2: Bounded Contexts

**What it is:** Different parts of the business may use the same word differently. Each "context" has its own model.

**Applied to our system:**

Our modules ARE bounded contexts:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        BOUNDED CONTEXTS                             │
│                                                                     │
│   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐            │
│   │   ORDER     │    │   PAYMENT   │    │  DELIVERY   │            │
│   │   CONTEXT   │    │   CONTEXT   │    │   CONTEXT   │            │
│   │             │    │             │    │             │            │
│   │  "Order"    │    │  "Payment"  │    │ "Delivery"  │            │
│   │  means the  │    │  means the  │    │  means the  │            │
│   │  customer's │    │  financial  │    │  physical   │            │
│   │  request    │    │  transaction│    │  transport  │            │
│   │             │    │             │    │             │            │
│   └─────────────┘    └─────────────┘    └─────────────┘            │
│                                                                     │
│   Each context has its own model of what matters                   │
│   They communicate through defined interfaces                       │
└─────────────────────────────────────────────────────────────────────┘
```

**Example:** The Order context cares about items, totals, and state. The Delivery context cares about addresses, drivers, and timing. They share an `orderId` but have different views of what's important.

#### Concept 3: Entities

**What it is:** Objects with identity that persists over time. Two entities with the same data but different IDs are different entities.

**In our system:**

```typescript
// Order is an ENTITY - has identity (orderId)
class Order {
  constructor(
    public readonly id: string,      // Identity
    private _state: OrderState,
    private _items: OrderItem[],
    // ...
  ) {}

  // Two orders with same items but different IDs are DIFFERENT orders
}

// Customer is an ENTITY
class Customer {
  constructor(
    public readonly id: string,      // Identity
    private _name: string,
    private _addresses: Address[],
  ) {}
}

// Driver is an ENTITY
class Driver {
  constructor(
    public readonly id: string,      // Identity
    private _restaurantId: string,
    private _isAvailable: boolean,
  ) {}
}
```

#### Concept 4: Value Objects

**What it is:** Objects defined by their attributes, not identity. Two value objects with the same data ARE the same.

**In our system:**

```typescript
// Money is a VALUE OBJECT - defined by amount and currency
class Money {
  constructor(
    public readonly amount: number,
    public readonly currency: string
  ) {}

  add(other: Money): Money {
    if (this.currency !== other.currency) {
      throw new Error('Cannot add different currencies');
    }
    return new Money(this.amount + other.amount, this.currency);
  }

  // Two Money(10, 'USD') ARE equal
  equals(other: Money): boolean {
    return this.amount === other.amount && this.currency === other.currency;
  }
}

// Address is a VALUE OBJECT
class Address {
  constructor(
    public readonly line: string,
    public readonly city: string,
    public readonly postcode: string
  ) {}

  // Immutable - to change, create new instance
}

// OrderItem is a VALUE OBJECT (within an Order)
class OrderItem {
  constructor(
    public readonly menuItemId: string,
    public readonly name: string,
    public readonly price: Money,
    public readonly quantity: number
  ) {}
}
```

#### Concept 5: Aggregates and Aggregate Roots

**What it is:** A cluster of entities and value objects treated as a single unit. The "root" is the entry point - you can only modify the aggregate through the root.

**In our system:**

```
┌─────────────────────────────────────────────────────────────────────┐
│                     ORDER AGGREGATE                                 │
│                                                                     │
│                    ┌─────────────────┐                             │
│                    │     ORDER       │ ◀── Aggregate Root          │
│                    │   (Entity)      │                             │
│                    └────────┬────────┘                             │
│                             │                                       │
│              ┌──────────────┼──────────────┐                       │
│              │              │              │                       │
│              ▼              ▼              ▼                       │
│        ┌──────────┐  ┌──────────┐  ┌──────────┐                   │
│        │OrderItem │  │OrderItem │  │ Address  │                   │
│        │(Value)   │  │(Value)   │  │ (Value)  │                   │
│        └──────────┘  └──────────┘  └──────────┘                   │
│                                                                     │
│   All changes to OrderItems go THROUGH Order                       │
│   You cannot modify OrderItem directly from outside                │
└─────────────────────────────────────────────────────────────────────┘
```

**In code:**

```typescript
class Order {
  private _items: OrderItem[];

  // Can only add items through the aggregate root
  addItem(menuItem: MenuItem, quantity: number): void {
    // Business rule: can only add items if order is in draft state
    if (this._state !== OrderState.DRAFT) {
      throw new Error('Cannot modify order after submission');
    }

    this._items.push(OrderItem.create(menuItem, quantity));
    this.recalculateTotals();
  }

  // Cannot access items directly for modification
  get items(): ReadonlyArray<OrderItem> {
    return [...this._items]; // Return copy
  }
}
```

**Other aggregates in our system:**

| Aggregate | Root | Contains |
|-----------|------|----------|
| Order | Order (entity) | OrderItems (value), Address (value) |
| Restaurant | Restaurant (entity) | MenuItems (entities) |
| Delivery | Delivery (entity) | - |
| Customer | Customer (entity) | Addresses (value) |

#### Concept 6: Domain Services

**What it is:** Operations that don't naturally belong to any entity. Business logic that spans multiple entities.

**In our system:**

```typescript
// Order total calculation spans Order + Restaurant (for delivery fee) + Platform (for fee %)
// This is a Domain Service

class OrderPricingService {
  calculateTotal(
    items: OrderItem[],
    restaurantDeliveryFee: Money,
    platformFeePercentage: number
  ): OrderPricing {
    const itemsTotal = items.reduce(
      (sum, item) => sum.add(item.price.multiply(item.quantity)),
      Money.zero('USD')
    );

    const subtotal = itemsTotal.add(restaurantDeliveryFee);
    const platformFee = subtotal.multiply(platformFeePercentage);
    const total = subtotal.add(platformFee);

    return new OrderPricing(itemsTotal, restaurantDeliveryFee, platformFee, total);
  }
}
```

#### Concept 7: Repository Pattern

**What it is:** Abstraction for storing and retrieving aggregates. Hides persistence details from domain.

**In our system (already using this):**

```typescript
// Interface in domain layer
interface OrderRepository {
  save(order: Order): Promise<void>;
  findById(id: string): Promise<Order | null>;
  findByCustomer(customerId: string): Promise<Order[]>;
}

// Implementation in infrastructure layer
class PostgresOrderRepository implements OrderRepository {
  async save(order: Order): Promise<void> {
    // Translate aggregate to database rows
  }

  async findById(id: string): Promise<Order | null> {
    // Translate database rows to aggregate
  }
}
```

### How DDD Fits Into Our Clean Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                 CLEAN ARCHITECTURE + DDD                            │
│                                                                     │
│   ┌─────────────────────────────────────────────────────────────┐  │
│   │                    PRESENTATION                              │  │
│   │              (Controllers, API endpoints)                    │  │
│   └───────────────────────────┬─────────────────────────────────┘  │
│                               │                                     │
│   ┌───────────────────────────▼─────────────────────────────────┐  │
│   │                    APPLICATION                               │  │
│   │              (Use Cases, Application Services)               │  │
│   │                                                              │  │
│   │   PlaceOrderUseCase, AcceptOrderUseCase, etc.               │  │
│   └───────────────────────────┬─────────────────────────────────┘  │
│                               │                                     │
│   ┌───────────────────────────▼─────────────────────────────────┐  │
│   │                      DOMAIN (DDD lives here)                 │  │
│   │                                                              │  │
│   │   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │  │
│   │   │  Entities   │  │   Value     │  │  Domain     │         │  │
│   │   │  (Order,    │  │  Objects    │  │  Services   │         │  │
│   │   │  Customer)  │  │  (Money,    │  │  (Pricing)  │         │  │
│   │   │             │  │   Address)  │  │             │         │  │
│   │   └─────────────┘  └─────────────┘  └─────────────┘         │  │
│   │                                                              │  │
│   │   ┌─────────────┐  ┌─────────────┐                          │  │
│   │   │ Aggregates  │  │ Repository  │                          │  │
│   │   │ (Order +    │  │ Interfaces  │                          │  │
│   │   │  Items)     │  │             │                          │  │
│   │   └─────────────┘  └─────────────┘                          │  │
│   │                                                              │  │
│   │   Ubiquitous Language used throughout                       │  │
│   └───────────────────────────┬─────────────────────────────────┘  │
│                               │                                     │
│   ┌───────────────────────────▼─────────────────────────────────┐  │
│   │                   INFRASTRUCTURE                             │  │
│   │           (Repository Implementations, External APIs)        │  │
│   └─────────────────────────────────────────────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Practical DDD for Our MVP

**What to use from DDD:**

| Concept | Use Now? | Why |
|---------|----------|-----|
| Ubiquitous Language | ✅ Yes | Free, just use consistent terms |
| Bounded Contexts | ✅ Yes | Our modules already are bounded contexts |
| Entities | ✅ Yes | Order, Customer, Driver need identity |
| Value Objects | ✅ Yes | Money, Address are cleaner as value objects |
| Aggregates | ✅ Simple | Order contains OrderItems |
| Repository Pattern | ✅ Yes | Already planned |
| Domain Services | ✅ When needed | OrderPricingService makes sense |
| Domain Events | ❌ Not yet | Add in Question 2 if needed |
| Event Sourcing | ❌ No | Overkill for MVP |
| CQRS | ❌ Not yet | Add in Question 2 for reporting |

**What this looks like in practice:**

```
src/
├── modules/
│   ├── order/
│   │   ├── presentation/
│   │   │   └── OrderController.ts
│   │   │
│   │   ├── application/
│   │   │   ├── PlaceOrderUseCase.ts
│   │   │   └── AcceptOrderUseCase.ts
│   │   │
│   │   ├── domain/                      # DDD concepts live here
│   │   │   ├── entities/
│   │   │   │   └── Order.ts             # Entity (aggregate root)
│   │   │   │
│   │   │   ├── value-objects/
│   │   │   │   ├── OrderItem.ts         # Value object
│   │   │   │   ├── Money.ts             # Value object
│   │   │   │   └── DeliveryAddress.ts   # Value object
│   │   │   │
│   │   │   ├── services/
│   │   │   │   └── OrderPricingService.ts  # Domain service
│   │   │   │
│   │   │   └── repositories/
│   │   │       └── IOrderRepository.ts  # Repository interface
│   │   │
│   │   └── infrastructure/
│   │       └── PostgresOrderRepository.ts
```

### The Value of DDD as a Modeling Tool

**For the team:**

1. **Shared understanding** - Everyone uses the same terms
2. **Code reflects business** - Reading code teaches you the domain
3. **Business rules in one place** - Order.accept() contains acceptance rules
4. **Clear boundaries** - Each aggregate is self-contained
5. **Testable domain** - Domain has no infrastructure dependencies

**Example conversation enabled by DDD:**

> Developer: "When an Order is accepted, what happens?"
> Business: "The Order moves to ACCEPTED state, and we process payment"
> Developer: "What if the Order is already rejected?"
> Business: "That's not allowed - you can only accept pending orders"

**This becomes code:**

```typescript
class Order {
  accept(): void {
    if (this._state !== OrderState.PENDING_ACCEPTANCE) {
      throw new InvalidStateTransitionError(
        `Cannot accept order in state ${this._state}`
      );
    }
    this._state = OrderState.ACCEPTED;
  }
}
```

The code IS the business rule. No translation needed.

---

## 11. References and Further Reading

### Layered / Clean Architecture

| Resource | Type | Link |
|----------|------|------|
| Clean Architecture by Robert C. Martin | Book | ISBN: 978-0134494166 |
| The Clean Architecture (Blog Post) | Article | https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html |
| Implementing Clean Architecture | Video | https://www.youtube.com/watch?v=SxJPQ5qXisw |

### Hexagonal / Ports and Adapters

| Resource | Type | Link |
|----------|------|------|
| Hexagonal Architecture by Alistair Cockburn | Article | https://alistair.cockburn.us/hexagonal-architecture/ |
| Ports and Adapters Pattern | Article | https://herbertograca.com/2017/09/14/ports-adapters-architecture/ |
| Growing Object-Oriented Software by Steve Freeman | Book | ISBN: 978-0321503626 |

### Vertical Slice Architecture

| Resource | Type | Link |
|----------|------|------|
| Vertical Slice Architecture by Jimmy Bogard | Article | https://jimmybogard.com/vertical-slice-architecture/ |
| CQRS and Vertical Slices | Talk | https://www.youtube.com/watch?v=SUiWfhAhgQw |
| MediatR (common implementation) | Library | https://github.com/jbogard/MediatR |

### Modular Monolith

| Resource | Type | Link |
|----------|------|------|
| Modular Monolith: A Primer by Kamil Grzybek | Article | https://www.kamilgrzybek.com/design/modular-monolith-primer/ |
| Modular Monolith Architecture | GitHub | https://github.com/kgrzybek/modular-monolith-with-ddd |
| Monolith to Microservices by Sam Newman | Book | ISBN: 978-1492047841 |

### Domain-Driven Design (Background)

| Resource | Type | Link |
|----------|------|------|
| Domain-Driven Design by Eric Evans | Book | ISBN: 978-0321125217 |
| Domain-Driven Design Quickly | Free Book | https://www.infoq.com/minibooks/domain-driven-design-quickly/ |
| Implementing DDD by Vaughn Vernon | Book | ISBN: 978-0321834577 |

### General Architecture Decision Making

| Resource | Type | Link |
|----------|------|------|
| Choose Boring Technology | Article | https://mcfunley.com/choose-boring-technology |
| Architecture Decision Records (ADRs) | Article | https://adr.github.io/ |
| Evolutionary Architecture by Neal Ford | Book | ISBN: 978-1491986363 |

### Practical Implementation Examples

| Resource | Type | Link |
|----------|------|------|
| .NET Clean Architecture Template | GitHub | https://github.com/jasontaylordev/CleanArchitecture |
| Node.js Clean Architecture | GitHub | https://github.com/royib/clean-architecture-node |
| Go Hexagonal Architecture | GitHub | https://github.com/ThreeDotsLabs/wild-workouts-go-ddd-example |

---

## Summary

### Why Pragmatic Layered/Clean for Our Modular Monolith

1. **Right amount of structure** for 3 developers building an MVP
2. **Familiar pattern** that most developers understand
3. **Testable** where it matters (business logic, external dependencies)
4. **Evolvable** - we can add more structure as we grow
5. **Defensible** - clear reasoning for every choice
6. **Low risk** - minimal overhead if business doesn't grow, saves us weeks if it does

### The Ceremony Question Answered

We are following **good practices**, not adding **excessive ceremony**.

The difference:
- **Good practice**: Separating concerns, isolating external dependencies, clear module boundaries
- **Excessive ceremony**: Interfaces for everything, ports for internal communication, domain events for simple CRUD

We're doing the first, not the second.

### If You Have to Defend This Choice

> "We chose a pragmatic layered architecture because it provides clear structure without excessive overhead. It isolates external dependencies like payment providers behind interfaces, which we know we'll need when scaling. The layers help our small team avoid conflicts and make the codebase accessible to new developers. We deliberately chose NOT to use full hexagonal architecture because we don't yet have the complexity that justifies its ceremony. We can evolve to more sophisticated patterns when and if the business requires it."
