# Internal Architecture Research: Layered, Clean, Hexagonal, and Pragmatic Approaches

## Document Purpose

This document provides in-depth research on internal architecture patterns - how code is organized *within* a deployable unit. This is distinct from deployment topology (monolith vs microservices) and focuses on the structure of business logic, dependencies, and separation of concerns.

While written in the context of our food delivery platform (Question 1), this serves as foundational research explaining what these patterns are, where they came from, and their trade-offs.

---

## Table of Contents

1. [What is Internal Architecture?](#1-what-is-internal-architecture)
2. [Traditional Layered Architecture](#2-traditional-layered-architecture)
3. [Clean Architecture](#3-clean-architecture)
4. [Hexagonal Architecture (Ports and Adapters)](#4-hexagonal-architecture-ports-and-adapters)
5. [Vertical Slice Architecture](#5-vertical-slice-architecture)
6. [Comparison of Patterns](#6-comparison-of-patterns)
7. [The Pragmatic Layered Approach](#7-the-pragmatic-layered-approach)
8. [Domain-Driven Design Integration](#8-domain-driven-design-integration)
9. [Application to Question 1](#9-application-to-question-1)
10. [References](#10-references)

---

## 1. What is Internal Architecture?

### 1.1 Definition

**Internal architecture** (also called "code architecture" or "application architecture") refers to how code is organized and structured within a single deployable unit. It answers questions like:
- How are responsibilities divided?
- What are the dependency rules?
- How is business logic separated from infrastructure?
- How do we organize files and folders?

### 1.2 Distinction from Deployment Topology

| Aspect | Deployment Topology | Internal Architecture |
|--------|--------------------|-----------------------|
| **Question answered** | How many deployable units? | How is code organized? |
| **Examples** | Monolith, Microservices | Layered, Clean, Hexagonal |
| **Scale** | System level | Application/Module level |
| **Visible to** | Operations, Infrastructure | Developers |

A monolith can use Clean Architecture internally. A microservice can use Layered Architecture. These are orthogonal concerns.

### 1.3 Why Internal Architecture Matters

Without intentional structure:
- Dependencies become tangled
- Business logic mixes with infrastructure
- Testing becomes difficult
- Changes ripple unpredictably
- New developers struggle to understand the codebase

Good internal architecture provides:
- Clear dependency direction
- Testable business logic
- Replaceable infrastructure
- Consistent patterns for developers
- Long-term maintainability

### 1.4 The Dependency Rule

Most modern architectures follow some form of the **dependency rule**:

> Dependencies should point inward toward business logic, not outward toward infrastructure.

This means:
- Business logic doesn't know about databases
- Business logic doesn't know about web frameworks
- Infrastructure depends on business rules, not vice versa

---

## 2. Traditional Layered Architecture

### 2.1 Origins and History

Layered Architecture is one of the oldest and most widely used patterns. It emerged from mainframe computing in the 1960s-70s and was formalized in the enterprise Java world in the 1990s.

The pattern appears in:
- The OSI networking model (7 layers)
- Classic n-tier web applications
- Java EE patterns (Presentation, Business, Data tiers)
- Microsoft's .NET guidance (early 2000s)

### 2.2 Core Concept

Organize code into horizontal layers, where each layer:
- Has a specific responsibility
- Only communicates with adjacent layers
- Depends only on layers below it

### 2.3 Classic 3-Layer Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TRADITIONAL 3-LAYER ARCHITECTURE                         │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                         PRESENTATION LAYER                                   │
│                                                                             │
│   - UI Components (Web pages, API controllers)                              │
│   - Input validation                                                        │
│   - User interaction handling                                               │
│   - View models / DTOs for display                                          │
│                                                                             │
│   Depends on: Business Layer                                                │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ calls
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          BUSINESS LAYER                                      │
│                                                                             │
│   - Business logic and rules                                                │
│   - Service classes                                                         │
│   - Domain entities                                                         │
│   - Use case orchestration                                                  │
│                                                                             │
│   Depends on: Data Layer                                                    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ calls
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            DATA LAYER                                        │
│                                                                             │
│   - Database access                                                         │
│   - Repository implementations                                              │
│   - External service clients                                                │
│   - File system access                                                      │
│                                                                             │
│   Depends on: Database, External APIs                                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
                              ┌───────────┐
                              │ Database  │
                              └───────────┘
```

### 2.4 The 4-Layer Variation

Many applications add an Application/Service layer:

```
┌───────────────────────────────────────────────────────────────────────┐
│  PRESENTATION       │  Controllers, Views, API Endpoints             │
└───────────────────────────────────────────────────────────────────────┘
                                    │
┌───────────────────────────────────────────────────────────────────────┐
│  APPLICATION        │  Use Cases, Application Services, DTOs         │
└───────────────────────────────────────────────────────────────────────┘
                                    │
┌───────────────────────────────────────────────────────────────────────┐
│  DOMAIN             │  Entities, Value Objects, Domain Services      │
└───────────────────────────────────────────────────────────────────────┘
                                    │
┌───────────────────────────────────────────────────────────────────────┐
│  INFRASTRUCTURE     │  Repositories, External APIs, Database         │
└───────────────────────────────────────────────────────────────────────┘
```

### 2.5 Characteristics

| Characteristic | Description |
|----------------|-------------|
| **Direction** | Top-to-bottom dependency flow |
| **Coupling** | Each layer coupled to layer below |
| **Business Logic** | Lives in Business/Domain layer |
| **Database Access** | Lives in Data/Infrastructure layer |
| **Testing** | Requires mocking lower layers |
| **Folder Structure** | Organized by technical layer |

### 2.6 Folder Structure Example

```
src/
├── presentation/           # or "controllers", "api", "web"
│   ├── OrderController
│   ├── CustomerController
│   └── RestaurantController
│
├── business/               # or "services", "application"
│   ├── OrderService
│   ├── CustomerService
│   └── RestaurantService
│
├── domain/                 # or "models", "entities"
│   ├── Order
│   ├── Customer
│   └── Restaurant
│
└── data/                   # or "repositories", "infrastructure"
    ├── OrderRepository
    ├── CustomerRepository
    └── RestaurantRepository
```

### 2.7 Advantages

1. **Simplicity**
   - Easy to understand
   - Well-known pattern
   - Most developers familiar with it
   - Low learning curve

2. **Clear Separation**
   - UI separate from business logic
   - Business logic separate from data access
   - Each layer has defined responsibility

3. **Technology Isolation**
   - Change database without affecting business logic
   - Change UI framework without affecting services

4. **Wide Support**
   - Frameworks designed for this pattern
   - Abundant examples and documentation
   - Standard in enterprise development

### 2.8 Disadvantages

1. **Business Logic Depends on Infrastructure**
   - Domain layer depends on data layer
   - Business rules coupled to database abstractions
   - Hard to test business logic in isolation

2. **Tendency to Become Anemic**
   - Business logic migrates to services
   - Domain entities become data containers
   - "Anemic Domain Model" antipattern

3. **Horizontal Organization**
   - Related code scattered across layers
   - To understand a feature, navigate multiple folders
   - Changes span many layers

4. **Layer Skipping**
   - Temptation to bypass layers for "efficiency"
   - Presentation calling data directly
   - Erodes the architecture over time

5. **All-or-Nothing Changes**
   - Database schema change affects all layers
   - New feature requires touching every layer

### 2.9 The "Infrastructure Dependency" Problem

The fundamental issue with traditional layered architecture:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    THE DEPENDENCY DIRECTION PROBLEM                         │
└─────────────────────────────────────────────────────────────────────────────┘

Traditional Layered:                  What We Want:

  Business Layer                        Business Logic
       │                                     ▲
       │ depends on                          │
       ▼                              depends on (inverted)
  Data Layer                                 │
       │                                Infrastructure
       ▼
  Database                            Business should be independent!

The business layer knows about        Business logic should know nothing
OrderRepository, which knows           about databases, repositories,
about SQL, databases, etc.            or external systems.
```

This problem led to the development of Clean and Hexagonal architectures.

---

## 3. Clean Architecture

### 3.1 Origins and History

**Clean Architecture** was proposed by Robert C. Martin (Uncle Bob) in 2012 through a series of blog posts, and formalized in his 2017 book "Clean Architecture: A Craftsman's Guide to Software Structure and Design."

It synthesizes ideas from:
- **Hexagonal Architecture** (Alistair Cockburn, 2005)
- **Onion Architecture** (Jeffrey Palermo, 2008)
- **BCE (Boundary-Control-Entity)** (Ivar Jacobson, 1992)
- **DCI (Data, Context, Interaction)** (Trygve Reenskaug)

Uncle Bob argued these are all the same architecture with different names.

### 3.2 Core Concept

The fundamental principle is **The Dependency Rule**:

> Source code dependencies must point only inward, toward higher-level policies.

"Higher-level policies" = business rules
"Lower-level details" = frameworks, databases, UI

### 3.3 The Concentric Circles

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CLEAN ARCHITECTURE                                   │
│                                                                             │
│   Dependencies point INWARD                                                 │
│   Inner circles know nothing about outer circles                            │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                                                                     │   │
│   │   FRAMEWORKS & DRIVERS (Outermost)                                  │   │
│   │   - Web frameworks (Express, ASP.NET, Spring)                       │   │
│   │   - Database libraries (Sequelize, Entity Framework)                │   │
│   │   - UI frameworks (React, Angular)                                  │   │
│   │   - External APIs                                                   │   │
│   │                                                                     │   │
│   │   ┌─────────────────────────────────────────────────────────────┐   │   │
│   │   │                                                             │   │   │
│   │   │   INTERFACE ADAPTERS                                        │   │   │
│   │   │   - Controllers                                             │   │   │
│   │   │   - Presenters                                              │   │   │
│   │   │   - Gateways (Repository Implementations)                   │   │   │
│   │   │                                                             │   │   │
│   │   │   ┌─────────────────────────────────────────────────────┐   │   │   │
│   │   │   │                                                     │   │   │   │
│   │   │   │   APPLICATION BUSINESS RULES                        │   │   │   │
│   │   │   │   - Use Cases / Interactors                         │   │   │   │
│   │   │   │   - Application Services                            │   │   │   │
│   │   │   │                                                     │   │   │   │
│   │   │   │   ┌─────────────────────────────────────────────┐   │   │   │   │
│   │   │   │   │                                             │   │   │   │   │
│   │   │   │   │   ENTERPRISE BUSINESS RULES (Innermost)     │   │   │   │   │
│   │   │   │   │   - Entities                                │   │   │   │   │
│   │   │   │   │   - Value Objects                           │   │   │   │   │
│   │   │   │   │   - Domain Services                         │   │   │   │   │
│   │   │   │   │                                             │   │   │   │   │
│   │   │   │   └─────────────────────────────────────────────┘   │   │   │   │
│   │   │   │                                                     │   │   │   │
│   │   │   └─────────────────────────────────────────────────────┘   │   │   │
│   │   │                                                             │   │   │
│   │   └─────────────────────────────────────────────────────────────┘   │   │
│   │                                                                     │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.4 The Four Circles Explained

#### Circle 1: Entities (Enterprise Business Rules)

The innermost circle contains:
- **Entities**: Objects that encapsulate enterprise-wide business rules
- **Value Objects**: Immutable objects defined by their attributes
- **Domain Services**: Stateless operations on domain concepts

These are the most stable, least likely to change. They know nothing about the application, the database, or the web.

```
// Entity example - pure business logic
class Order {
    private items: OrderItem[];
    private status: OrderStatus;

    addItem(item: MenuItem, quantity: number): void {
        if (this.status !== OrderStatus.Draft) {
            throw new Error("Cannot modify submitted order");
        }
        this.items.push(new OrderItem(item, quantity));
    }

    calculateTotal(): Money {
        return this.items.reduce(
            (sum, item) => sum.add(item.subtotal()),
            Money.zero()
        );
    }
}
```

#### Circle 2: Use Cases (Application Business Rules)

Contains application-specific business rules:
- **Use Cases / Interactors**: Orchestrate the flow of data to and from entities
- Implement specific application operations
- Define input and output ports (interfaces)

```
// Use Case example
class PlaceOrderUseCase {
    constructor(
        private orderRepository: OrderRepository,  // interface
        private paymentGateway: PaymentGateway,    // interface
        private notifier: CustomerNotifier         // interface
    ) {}

    execute(input: PlaceOrderInput): PlaceOrderOutput {
        const order = this.orderRepository.findById(input.orderId);
        order.submit();

        const payment = this.paymentGateway.charge(order.total);
        order.markPaid(payment.reference);

        this.orderRepository.save(order);
        this.notifier.notifyOrderPlaced(order);

        return new PlaceOrderOutput(order.id, order.status);
    }
}
```

#### Circle 3: Interface Adapters

Converts data between use cases and external formats:
- **Controllers**: Handle HTTP requests, call use cases
- **Presenters**: Format data for display
- **Gateways**: Implement repository interfaces with real databases

```
// Controller (Interface Adapter)
class OrderController {
    constructor(private placeOrderUseCase: PlaceOrderUseCase) {}

    @Post('/orders/:id/place')
    placeOrder(req: Request): Response {
        const input = new PlaceOrderInput(req.params.id);
        const output = this.placeOrderUseCase.execute(input);
        return new Response(200, { orderId: output.orderId });
    }
}

// Repository Implementation (Interface Adapter)
class PostgresOrderRepository implements OrderRepository {
    findById(id: OrderId): Order {
        const row = this.db.query('SELECT * FROM orders WHERE id = $1', [id]);
        return this.mapToEntity(row);
    }
}
```

#### Circle 4: Frameworks & Drivers

The outermost circle:
- Web frameworks (Express, Spring, ASP.NET)
- Database drivers (PostgreSQL, MongoDB drivers)
- UI frameworks
- External libraries

This code is "glue" that connects to interface adapters.

### 3.5 Dependency Inversion in Practice

The key mechanism is **Dependency Inversion** using interfaces:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    HOW DEPENDENCY INVERSION WORKS                           │
└─────────────────────────────────────────────────────────────────────────────┘

WITHOUT Dependency Inversion:

    ┌─────────────┐          ┌─────────────────────┐
    │  Use Case   │─────────▶│ PostgresRepository  │
    │             │          │                     │
    └─────────────┘          └─────────────────────┘

    Use Case depends on concrete PostgreSQL implementation
    Can't test without database
    Can't swap databases


WITH Dependency Inversion:

    ┌─────────────┐          ┌─────────────────────┐
    │  Use Case   │─────────▶│  <<interface>>      │
    │             │          │  OrderRepository    │
    └─────────────┘          └──────────┬──────────┘
                                        ▲
                                        │ implements
                                        │
                             ┌──────────┴──────────┐
                             │ PostgresRepository  │
                             │                     │
                             └─────────────────────┘

    Use Case depends on abstraction (interface)
    Implementation depends on interface
    Both point toward the abstraction

    Can test with mock repository
    Can swap PostgreSQL for MongoDB
    Business logic is isolated
```

### 3.6 Folder Structure Example

```
src/
├── domain/                     # Entities - Circle 1
│   ├── order/
│   │   ├── Order.ts
│   │   ├── OrderItem.ts
│   │   └── OrderStatus.ts
│   ├── customer/
│   │   └── Customer.ts
│   └── shared/
│       ├── Money.ts
│       └── EntityId.ts
│
├── application/                # Use Cases - Circle 2
│   ├── order/
│   │   ├── PlaceOrderUseCase.ts
│   │   ├── PlaceOrderInput.ts
│   │   ├── PlaceOrderOutput.ts
│   │   └── ports/
│   │       ├── OrderRepository.ts      # Interface
│   │       └── PaymentGateway.ts       # Interface
│   └── customer/
│       └── RegisterCustomerUseCase.ts
│
├── adapters/                   # Interface Adapters - Circle 3
│   ├── controllers/
│   │   └── OrderController.ts
│   ├── presenters/
│   │   └── OrderPresenter.ts
│   └── gateways/
│       ├── PostgresOrderRepository.ts  # Implements interface
│       └── StripePaymentGateway.ts     # Implements interface
│
└── infrastructure/             # Frameworks & Drivers - Circle 4
    ├── web/
    │   └── ExpressApp.ts
    ├── database/
    │   └── PostgresConnection.ts
    └── config/
        └── DependencyInjection.ts
```

### 3.7 Advantages

1. **Independent of Frameworks**
   - Business logic doesn't depend on Express, Spring, etc.
   - Can swap frameworks without touching business rules

2. **Testable**
   - Business logic tested without UI, database, or external systems
   - Fast unit tests with mocks

3. **Independent of UI**
   - UI can change without affecting business rules
   - Web, mobile, CLI all use same use cases

4. **Independent of Database**
   - Swap PostgreSQL for MongoDB
   - Business rules don't know about SQL

5. **Independent of External Agencies**
   - Business rules don't know about external services
   - Easy to mock for testing

6. **Clear Dependency Direction**
   - All dependencies point inward
   - Inner layers are stable

### 3.8 Disadvantages

1. **Complexity**
   - More files, folders, and indirection
   - Interface for everything
   - Mapping between layers

2. **Ceremony**
   - Input/Output DTOs for every use case
   - Repository interfaces and implementations
   - More boilerplate code

3. **Over-engineering Risk**
   - Overkill for simple CRUD
   - Costs may not be justified for small apps

4. **Learning Curve**
   - Developers need to understand the rules
   - Easy to violate principles accidentally

5. **Navigation Difficulty**
   - Following a request through layers is harder
   - More jumping between files

---

## 4. Hexagonal Architecture (Ports and Adapters)

### 4.1 Origins and History

**Hexagonal Architecture**, also known as "Ports and Adapters," was introduced by Alistair Cockburn in 2005. The hexagonal shape is arbitrary - it just allows drawing multiple ports.

Cockburn's motivation:
> "Allow an application to equally be driven by users, programs, automated test or batch scripts, and to be developed and tested in isolation from its eventual run-time devices and databases."

### 4.2 Core Concept

The application is at the center, surrounded by ports (interfaces) that define how the outside world interacts with it. Adapters implement these ports for specific technologies.

**Two types of ports:**
- **Driving Ports (Primary)**: The application exposes these; external actors use them
- **Driven Ports (Secondary)**: The application uses these; external systems implement them

### 4.3 Visual Representation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    HEXAGONAL ARCHITECTURE                                    │
│                    (Ports and Adapters)                                      │
└─────────────────────────────────────────────────────────────────────────────┘

                      DRIVING SIDE                    DRIVEN SIDE
                      (Primary)                       (Secondary)

                      Actors that                     Systems the
                      USE the app                     app USES

    ┌──────────────┐                                          ┌──────────────┐
    │   REST API   │                                          │   Database   │
    │   Adapter    │                                          │   Adapter    │
    └──────┬───────┘                                          └───────┬──────┘
           │                                                          │
           ▼                                                          │
    ┌──────────────┐      ┌─────────────────────────┐      ┌─────────┴────────┐
    │              │      │                         │      │                  │
    │   Driving    │─────▶│      APPLICATION        │─────▶│    Driven        │
    │    Port      │      │        CORE             │      │     Port         │
    │  (Interface) │      │                         │      │   (Interface)    │
    │              │      │  - Domain Logic         │      │                  │
    └──────────────┘      │  - Use Cases            │      └──────────────────┘
           ▲              │  - Business Rules       │               │
           │              │                         │               ▼
    ┌──────┴───────┐      └─────────────────────────┘      ┌──────────────────┐
    │     CLI      │                                       │    Payment       │
    │   Adapter    │                                       │    Adapter       │
    └──────────────┘                                       └──────────────────┘
           ▲                                                        │
           │                                                        ▼
    ┌──────┴───────┐                                       ┌──────────────────┐
    │    Test      │                                       │    Email         │
    │   Adapter    │                                       │    Adapter       │
    └──────────────┘                                       └──────────────────┘


    Driving Adapters                                        Driven Adapters
    IMPLEMENT the                                           IMPLEMENT the
    driving ports                                           driven ports
    and CALL the                                            and ARE CALLED
    application                                             BY the application
```

### 4.4 Ports and Adapters Explained

#### Driving Ports (Primary Ports)
- Define how the application can be used
- Implemented BY the application
- Called BY adapters (controllers, CLI, tests)

```typescript
// Driving Port - the application exposes this
interface OrderService {
    placeOrder(customerId: string, items: OrderItemDto[]): OrderId;
    cancelOrder(orderId: string): void;
    getOrderStatus(orderId: string): OrderStatus;
}

// Application implements this port
class OrderServiceImpl implements OrderService {
    placeOrder(customerId: string, items: OrderItemDto[]): OrderId {
        // business logic
    }
}
```

#### Driven Ports (Secondary Ports)
- Define what the application needs from external systems
- Called BY the application
- Implemented BY adapters (repositories, API clients)

```typescript
// Driven Port - the application needs this
interface OrderRepository {
    save(order: Order): void;
    findById(id: OrderId): Order | null;
}

// Adapter implements this port
class PostgresOrderRepository implements OrderRepository {
    save(order: Order): void {
        this.db.query('INSERT INTO orders...');
    }
}
```

### 4.5 The Hexagon Shape

Why a hexagon? Cockburn:
> "The hexagon is intended to visually highlight:
> (a) the inside-outside asymmetry and
> (b) the similar nature of ports, to get away from one-dimensional thinking about top and bottom layers."

It's not about having six sides - it's about showing multiple ports.

### 4.6 Folder Structure Example

```
src/
├── application/                    # The Hexagon Core
│   ├── domain/
│   │   ├── Order.ts
│   │   └── OrderItem.ts
│   ├── services/
│   │   ├── OrderService.ts         # Driving Port Implementation
│   │   └── PaymentService.ts
│   └── ports/
│       ├── driving/
│       │   └── OrderServicePort.ts      # Driving Port Interface
│       └── driven/
│           ├── OrderRepositoryPort.ts   # Driven Port Interface
│           └── PaymentGatewayPort.ts    # Driven Port Interface
│
├── adapters/
│   ├── driving/                    # Driving Adapters
│   │   ├── rest/
│   │   │   └── OrderController.ts
│   │   ├── cli/
│   │   │   └── OrderCLI.ts
│   │   └── test/
│   │       └── OrderTestDriver.ts
│   │
│   └── driven/                     # Driven Adapters
│       ├── persistence/
│       │   └── PostgresOrderRepository.ts
│       ├── payment/
│       │   └── StripePaymentGateway.ts
│       └── notification/
│           └── EmailNotificationAdapter.ts
│
└── main/                           # Composition Root
    └── Application.ts
```

### 4.7 Advantages

1. **Symmetric View**
   - Both sides (driving and driven) are adapters
   - No "top" and "bottom" - just inside and outside

2. **Technology Agnostic Core**
   - Application doesn't know about HTTP, databases, etc.
   - Pure business logic in the center

3. **Test Friendly**
   - Test adapters can drive the application
   - Mock adapters can simulate external systems

4. **Multiple Entry Points**
   - Same application usable via REST, CLI, message queue
   - Just add another driving adapter

5. **Swappable Infrastructure**
   - Change database by swapping adapter
   - Change payment provider by swapping adapter

### 4.8 Disadvantages

1. **Conceptual Overhead**
   - "Ports," "Adapters," "Driving," "Driven"
   - More terminology to learn

2. **Interface Proliferation**
   - Port interface for every external interaction
   - Can feel excessive for simple apps

3. **Rigid Structure**
   - Everything must go through ports
   - Can slow down simple changes

4. **Same Costs as Clean Architecture**
   - More files, more indirection
   - Mapping between boundaries

### 4.9 Relationship to Clean Architecture

Clean Architecture and Hexagonal Architecture are essentially the same pattern with different visualizations:

| Clean Architecture | Hexagonal Architecture |
|-------------------|------------------------|
| Use Cases | Application Core |
| Controller | Driving Adapter |
| Gateway/Repository | Driven Adapter |
| Input Port | Driving Port |
| Output Port | Driven Port |
| Dependency Rule | Dependencies point inward |

Uncle Bob acknowledges Hexagonal Architecture as a precursor to Clean Architecture.

---

## 5. Vertical Slice Architecture

### 5.1 Origins and History

**Vertical Slice Architecture** was popularized by Jimmy Bogard (creator of AutoMapper and MediatR) around 2015-2018. It arose as a reaction to the ceremony of layered architectures.

Bogard's observation:
> "Traditional architectures organize by layer. But when you work on a feature, you touch every layer. Why not organize by feature instead?"

### 5.2 Core Concept

Instead of organizing by technical layer (controllers, services, repositories), organize by **feature** or **use case**. Each vertical slice is a complete mini-application.

### 5.3 Visual Comparison

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    LAYERED VS VERTICAL SLICE                                │
└─────────────────────────────────────────────────────────────────────────────┘

LAYERED ARCHITECTURE:                 VERTICAL SLICE ARCHITECTURE:

┌─────────────────────────────┐       ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐
│        Controllers          │       │Feature│ │Feature│ │Feature│ │Feature│
├─────────────────────────────┤       │   A   │ │   B   │ │   C   │ │   D   │
│         Services            │       │       │ │       │ │       │ │       │
├─────────────────────────────┤       │ ───── │ │ ───── │ │ ───── │ │ ───── │
│        Repositories         │       │Handler│ │Handler│ │Handler│ │Handler│
├─────────────────────────────┤       │ ───── │ │ ───── │ │ ───── │ │ ───── │
│         Entities            │       │  DB   │ │  DB   │ │  DB   │ │  DB   │
└─────────────────────────────┘       └───────┘ └───────┘ └───────┘ └───────┘

To add a feature:                     To add a feature:
- Add to Controllers                  - Add a new slice
- Add to Services                     - All code in one place
- Add to Repositories                 - Independent of other features
- Touch 4 layers                      - Touch 1 slice
```

### 5.4 Folder Structure Example

```
src/
├── Features/
│   ├── Orders/
│   │   ├── PlaceOrder/
│   │   │   ├── PlaceOrderCommand.ts      # Request DTO
│   │   │   ├── PlaceOrderHandler.ts      # All logic here
│   │   │   ├── PlaceOrderValidator.ts    # Validation
│   │   │   └── PlaceOrderResponse.ts     # Response DTO
│   │   │
│   │   ├── GetOrderStatus/
│   │   │   ├── GetOrderStatusQuery.ts
│   │   │   ├── GetOrderStatusHandler.ts
│   │   │   └── GetOrderStatusResponse.ts
│   │   │
│   │   └── CancelOrder/
│   │       ├── CancelOrderCommand.ts
│   │       └── CancelOrderHandler.ts
│   │
│   └── Customers/
│       ├── RegisterCustomer/
│       │   ├── RegisterCustomerCommand.ts
│       │   └── RegisterCustomerHandler.ts
│       │
│       └── GetCustomerProfile/
│           ├── GetCustomerProfileQuery.ts
│           └── GetCustomerProfileHandler.ts
│
├── Domain/                          # Shared entities (optional)
│   ├── Order.ts
│   └── Customer.ts
│
└── Infrastructure/                  # Shared infrastructure (optional)
    ├── Database.ts
    └── Mediator.ts
```

### 5.5 Handler Example (MediatR Pattern)

```typescript
// PlaceOrderCommand.ts
class PlaceOrderCommand {
    customerId: string;
    items: Array<{ itemId: string; quantity: number }>;
}

// PlaceOrderHandler.ts - ALL LOGIC IN ONE PLACE
class PlaceOrderHandler {
    constructor(private db: Database) {}

    async handle(command: PlaceOrderCommand): Promise<PlaceOrderResponse> {
        // Validation (could be separate validator)
        if (command.items.length === 0) {
            throw new Error("Order must have items");
        }

        // Load data
        const customer = await this.db.customers.findById(command.customerId);
        const menuItems = await this.db.menuItems.findByIds(
            command.items.map(i => i.itemId)
        );

        // Business logic
        const order = Order.create(customer);
        for (const item of command.items) {
            const menuItem = menuItems.find(m => m.id === item.itemId);
            order.addItem(menuItem, item.quantity);
        }

        // Persist
        await this.db.orders.save(order);

        // Return
        return new PlaceOrderResponse(order.id, order.total);
    }
}
```

### 5.6 Advantages

1. **Feature Cohesion**
   - All code for a feature in one place
   - Easy to understand a feature
   - Easy to delete a feature

2. **Independent Slices**
   - Change one feature without affecting others
   - Each slice can have different patterns
   - Complex features can use Clean Architecture
   - Simple features can be straightforward

3. **Less Abstraction**
   - No forced repository interfaces
   - No service layer indirection
   - Handler can access database directly if appropriate

4. **Faster Development**
   - Less ceremony
   - Less jumping between files
   - New feature = new folder

5. **Scales with Team**
   - Different developers own different features
   - Less merge conflicts
   - Clearer ownership

### 5.7 Disadvantages

1. **Code Duplication**
   - Similar logic may be repeated across slices
   - No shared service layer
   - Refactoring to shared code is manual

2. **Inconsistency Risk**
   - Each slice can do things differently
   - Team must agree on conventions
   - Code review important

3. **Cross-Cutting Concerns**
   - Logging, authentication, etc. need different approach
   - Often handled via middleware/decorators

4. **Shared Domain Logic**
   - If entities have rich behavior, where do they live?
   - May need a shared Domain layer anyway

5. **Database Coupling**
   - Handlers often access database directly
   - Harder to swap infrastructure
   - Testing may require real database

### 5.8 When to Use Vertical Slices

Works well when:
- Features are independent
- CRUD-heavy application
- Team wants to move fast
- Less emphasis on infrastructure swapping
- Different features may need different patterns

Works less well when:
- Significant shared business logic
- Entities have rich domain behavior
- Need to swap infrastructure frequently
- Strong consistency needs across features

---

## 6. Comparison of Patterns

### 6.1 Organizing Principle Comparison

| Pattern | Organizing Principle | Folder Structure |
|---------|---------------------|------------------|
| **Layered** | Technical responsibility | By layer (controllers, services, repos) |
| **Clean** | Dependency direction | By circle (domain, application, infrastructure) |
| **Hexagonal** | Port/Adapter symmetry | By adapter type (driving, driven) |
| **Vertical Slice** | Feature/Use Case | By feature (PlaceOrder, GetCustomer) |

### 6.2 Dependency Direction

| Pattern | Dependency Rule |
|---------|-----------------|
| **Layered** | Top to bottom (Presentation → Business → Data) |
| **Clean** | Inward (all circles depend on Entities) |
| **Hexagonal** | Inward (adapters depend on ports/core) |
| **Vertical Slice** | Per-slice (varies) |

### 6.3 Abstraction Requirements

| Pattern | Interfaces Required? |
|---------|---------------------|
| **Layered** | Optional (often used anyway) |
| **Clean** | Yes - ports for everything |
| **Hexagonal** | Yes - ports for everything |
| **Vertical Slice** | Optional (per slice decision) |

### 6.4 Testing Approach

| Pattern | Testing Strategy |
|---------|------------------|
| **Layered** | Mock lower layers |
| **Clean** | Mock ports, test use cases in isolation |
| **Hexagonal** | Test adapters swap for test doubles |
| **Vertical Slice** | Integration tests per slice |

### 6.5 Ceremony vs Flexibility Trade-off

```
More Ceremony                                        Less Ceremony
More Flexibility                                     Less Flexibility
     │                                                    │
     ▼                                                    ▼
┌──────────┐    ┌──────────────┐    ┌──────────┐    ┌─────────────┐
│  Clean   │    │  Hexagonal   │    │  Layered │    │  Vertical   │
│          │    │              │    │          │    │    Slice    │
│ Strictest│    │ Similar to   │    │ Simple   │    │  Least      │
│ rules    │    │ Clean        │    │ Layers   │    │  structured │
└──────────┘    └──────────────┘    └──────────┘    └─────────────┘
```

---

## 7. The Pragmatic Layered Approach

### 7.1 What is "Pragmatic Layered"?

**Pragmatic Layered Architecture** is not a formal pattern but a practical approach that:
- Uses the familiar layered structure
- Incorporates dependency inversion where valuable
- Avoids excessive ceremony
- Focuses on the 80% case, not edge cases

It's essentially: "Use layers, but use interfaces where they matter."

### 7.2 Core Principles

1. **Keep the familiar layer structure**
   - Developers know layers; leverage that

2. **Apply dependency inversion selectively**
   - Interfaces for database access (testing, swapping)
   - Interfaces for external APIs (testing, swapping)
   - No interface for internal services if not needed

3. **Don't add abstraction without reason**
   - If it doesn't enable testing or swapping, skip it
   - YAGNI (You Aren't Gonna Need It)

4. **Domain can depend on nothing**
   - Domain entities are pure
   - This is the one Clean Architecture principle we keep

### 7.3 Structure

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PRAGMATIC LAYERED ARCHITECTURE                           │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                         PRESENTATION LAYER                                   │
│   Controllers, API endpoints                                                │
│   Depends on: Application Layer (directly)                                  │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ calls
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         APPLICATION LAYER                                    │
│   Services, Use Cases                                                       │
│   Depends on: Domain Layer, Repository Interfaces                           │
│                                                                             │
│   ┌───────────────────────────────────────────────────────────────┐         │
│   │  Repository Interfaces defined here (not in Infrastructure)   │         │
│   │  OrderRepository, CustomerRepository                           │         │
│   └───────────────────────────────────────────────────────────────┘         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
          ┌─────────────────────────┴─────────────────────────┐
          │                                                   │
          ▼                                                   ▼
┌─────────────────────────────────┐    ┌─────────────────────────────────────┐
│         DOMAIN LAYER            │    │       INFRASTRUCTURE LAYER          │
│                                 │    │                                     │
│   Entities, Value Objects       │    │   Repository Implementations        │
│   Domain Services               │    │   External API Clients              │
│                                 │    │   Payment Gateway Implementation    │
│   Pure business logic           │    │                                     │
│   NO dependencies               │    │   Implements interfaces from        │
│                                 │    │   Application Layer                 │
└─────────────────────────────────┘    └─────────────────────────────────────┘
```

### 7.4 Key Difference from Traditional Layered

The key insight: **Repository interfaces live in the Application layer, not Infrastructure**.

```
TRADITIONAL:                         PRAGMATIC:

Application Layer                    Application Layer
      │                                   │
      │ depends on                        │ depends on
      ▼                                   ▼
Infrastructure Layer              Repository Interface (in App layer)
                                          ▲
                                          │ implements
                                          │
                                  Infrastructure Layer

Application depends on concrete    Application depends on abstraction
infrastructure                     Infrastructure depends on abstraction
                                  Dependencies point UPWARD
```

### 7.5 When to Use Interfaces

| Component | Interface? | Reason |
|-----------|------------|--------|
| **Repository (database access)** | Yes | Testing, potential database swap |
| **External API client** | Yes | Testing, provider swap |
| **Payment gateway** | Yes | Testing, provider swap |
| **Email service** | Yes | Testing, provider swap |
| **Internal service** | Usually No | YAGNI, adds noise |
| **Utility classes** | No | Pure functions, easy to test |

### 7.6 Folder Structure Example

```
src/
├── presentation/               # Controllers
│   └── controllers/
│       ├── OrderController.ts
│       └── CustomerController.ts
│
├── application/                # Services + Interfaces
│   ├── services/
│   │   ├── OrderService.ts
│   │   └── CustomerService.ts
│   └── ports/                  # Interface definitions here
│       ├── OrderRepository.ts
│       ├── PaymentGateway.ts
│       └── NotificationService.ts
│
├── domain/                     # Pure business logic
│   ├── Order.ts
│   ├── OrderItem.ts
│   ├── Customer.ts
│   └── Money.ts
│
└── infrastructure/             # Implementations
    ├── repositories/
    │   ├── PostgresOrderRepository.ts    # implements OrderRepository
    │   └── PostgresCustomerRepository.ts
    ├── payment/
    │   └── StripePaymentGateway.ts       # implements PaymentGateway
    └── notifications/
        └── PushNotificationService.ts    # implements NotificationService
```

### 7.7 Why This Works Well with Modular Monolith

The Pragmatic Layered approach pairs excellently with a Modular Monolith because:

1. **Same Pattern Per Module**
   - Each module uses the same internal structure
   - Consistent for developers
   - Each module is a "mini application"

2. **Module Independence**
   - Module A's infrastructure doesn't affect Module B
   - Modules communicate through application layer interfaces

3. **Shared Infrastructure When Needed**
   - Common database connection
   - Common notification service
   - But each module has its own repositories

4. **Prepared for Extraction**
   - If Module A needs to become a service, interfaces already exist
   - Just implement interfaces with network calls instead of DB calls

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MODULAR MONOLITH + PRAGMATIC LAYERED                     │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────┐ ┌─────────────────────────┐ ┌─────────────────────┐
│     ORDER MODULE        │ │    CUSTOMER MODULE      │ │   DELIVERY MODULE   │
│                         │ │                         │ │                     │
│ ┌─────────────────────┐ │ │ ┌─────────────────────┐ │ │ ┌─────────────────┐ │
│ │    Presentation     │ │ │ │    Presentation     │ │ │ │   Presentation  │ │
│ └─────────────────────┘ │ │ └─────────────────────┘ │ │ └─────────────────┘ │
│ ┌─────────────────────┐ │ │ ┌─────────────────────┐ │ │ ┌─────────────────┐ │
│ │    Application      │ │ │ │    Application      │ │ │ │   Application   │ │
│ │    + Ports          │ │ │ │    + Ports          │ │ │ │   + Ports       │ │
│ └─────────────────────┘ │ │ └─────────────────────┘ │ │ └─────────────────┘ │
│ ┌─────────────────────┐ │ │ ┌─────────────────────┐ │ │ ┌─────────────────┐ │
│ │      Domain         │ │ │ │      Domain         │ │ │ │     Domain      │ │
│ └─────────────────────┘ │ │ └─────────────────────┘ │ │ └─────────────────┘ │
│ ┌─────────────────────┐ │ │ ┌─────────────────────┐ │ │ ┌─────────────────┐ │
│ │   Infrastructure    │ │ │ │   Infrastructure    │ │ │ │  Infrastructure │ │
│ └─────────────────────┘ │ │ └─────────────────────┘ │ │ └─────────────────┘ │
│                         │ │                         │ │                     │
│  Owns: Order, Payment   │ │ Owns: Customer, Address │ │ Owns: Driver, Shift │
│        tables           │ │        tables           │ │       tables        │
└────────────┬────────────┘ └────────────┬────────────┘ └──────────┬──────────┘
             │                           │                          │
             └───────────────────────────┼──────────────────────────┘
                                         │
                                         ▼
                            ┌────────────────────────┐
                            │   PostgreSQL Database  │
                            │   (One database,       │
                            │    owned tables)       │
                            └────────────────────────┘
```

---

## 8. Domain-Driven Design Integration

### 8.1 What is Domain-Driven Design?

**Domain-Driven Design (DDD)** is an approach to software development introduced by Eric Evans in his 2003 book "Domain-Driven Design: Tackling Complexity in the Heart of Software."

DDD is NOT an architecture pattern. It's a modeling approach that provides:
- **Tactical patterns**: Entity, Value Object, Aggregate, Repository, Domain Service
- **Strategic patterns**: Bounded Context, Ubiquitous Language, Context Mapping

### 8.2 DDD Tactical Patterns

| Pattern | Description |
|---------|-------------|
| **Entity** | Object with identity that persists over time |
| **Value Object** | Immutable object defined by its attributes |
| **Aggregate** | Cluster of entities with a root entity |
| **Repository** | Abstraction for aggregate persistence |
| **Domain Service** | Stateless operation on domain concepts |
| **Factory** | Creates complex objects/aggregates |
| **Domain Event** | Something that happened in the domain |

### 8.3 DDD and Internal Architecture

DDD tactical patterns can be used within any architecture:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DDD PATTERNS IN PRAGMATIC LAYERED                        │
└─────────────────────────────────────────────────────────────────────────────┘

                        Presentation Layer
                              │
                              ▼
                    ┌─────────────────────┐
                    │  Application Layer  │
                    │                     │
                    │  - Application      │
                    │    Services         │
                    │  - Use Cases        │
                    │                     │
                    │  Repository         │ ◀── Repository Interface (DDD pattern)
                    │  Interfaces         │
                    └─────────┬───────────┘
                              │
                ┌─────────────┴─────────────┐
                │                           │
                ▼                           ▼
       ┌─────────────────┐        ┌─────────────────────┐
       │  Domain Layer   │        │ Infrastructure Layer│
       │                 │        │                     │
       │  - Entities     │        │  - Repository       │
       │  - Value Objects│        │    Implementations  │
       │  - Aggregates   │        │                     │
       │  - Domain       │        │                     │
       │    Services     │        │                     │
       │  - Domain Events│        │                     │
       │                 │        │                     │
       │  (DDD Tactical  │        │                     │
       │   Patterns)     │        │                     │
       └─────────────────┘        └─────────────────────┘
```

### 8.4 Entity vs Anemic Domain Model

**Rich Domain Model** (DDD):
```typescript
class Order {
    private items: OrderItem[];
    private status: OrderStatus;

    addItem(menuItem: MenuItem, quantity: number): void {
        this.ensureDraft();
        this.items.push(new OrderItem(menuItem, quantity));
    }

    submit(): void {
        this.ensureDraft();
        this.ensureHasItems();
        this.status = OrderStatus.Submitted;
        this.recordEvent(new OrderSubmittedEvent(this.id));
    }

    private ensureDraft(): void {
        if (this.status !== OrderStatus.Draft) {
            throw new InvalidOperationError("Order is not a draft");
        }
    }
}
```

**Anemic Domain Model** (anti-pattern):
```typescript
// Entity is just data
class Order {
    items: OrderItem[];
    status: OrderStatus;
}

// All logic in service
class OrderService {
    addItem(order: Order, menuItem: MenuItem, quantity: number): void {
        if (order.status !== OrderStatus.Draft) {
            throw new Error("Cannot modify");
        }
        order.items.push({ menuItemId: menuItem.id, quantity });
    }

    submit(order: Order): void {
        if (order.status !== OrderStatus.Draft) {
            throw new Error("Cannot submit");
        }
        if (order.items.length === 0) {
            throw new Error("No items");
        }
        order.status = OrderStatus.Submitted;
    }
}
```

The rich model encapsulates business rules within the entity itself.

### 8.5 Aggregates

An **Aggregate** is a cluster of domain objects treated as a unit:
- Has an **Aggregate Root** (the entry point)
- External objects reference only the root
- Consistency is enforced within the aggregate

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ORDER AGGREGATE                                    │
│                                                                             │
│                         ┌──────────────────┐                                │
│                         │      Order       │ ◀── Aggregate Root             │
│                         │  (Aggregate Root)│                                │
│                         └────────┬─────────┘                                │
│                                  │                                          │
│                    ┌─────────────┼─────────────┐                            │
│                    │             │             │                            │
│                    ▼             ▼             ▼                            │
│            ┌───────────┐ ┌───────────┐ ┌───────────┐                       │
│            │OrderItem 1│ │OrderItem 2│ │OrderItem 3│                       │
│            └───────────┘ └───────────┘ └───────────┘                       │
│                                                                             │
│   - Order is the root; all access goes through Order                       │
│   - OrderItems cannot be accessed directly from outside                     │
│   - One Repository per Aggregate (OrderRepository, not OrderItemRepository) │
│   - Consistency boundary: All items saved/loaded together                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 8.6 Value Objects

**Value Objects** are defined by their attributes, not identity:

```typescript
// Value Object: Money
class Money {
    constructor(
        private readonly amount: number,
        private readonly currency: Currency
    ) {}

    add(other: Money): Money {
        if (this.currency !== other.currency) {
            throw new Error("Currency mismatch");
        }
        return new Money(this.amount + other.amount, this.currency);
    }

    equals(other: Money): boolean {
        return this.amount === other.amount
            && this.currency === other.currency;
    }
}

// Usage
const price = new Money(10.99, Currency.USD);
const total = price.add(new Money(5.00, Currency.USD));
// total is a NEW Money object (immutable)
```

### 8.7 When to Use DDD

DDD adds value when:
- Domain logic is complex
- Business rules are nuanced
- Domain experts available for collaboration
- Long-lived application

DDD may be overkill when:
- Simple CRUD application
- Technical domain (no business experts)
- Short-lived or prototype application
- Very small team without DDD experience

---

## 9. Application to Question 1

### 9.1 Our Constraints Recap

| Constraint | Value |
|------------|-------|
| Restaurants | 1 |
| Developers | 3 |
| Budget | Very limited |
| DevOps team | None |
| Goal | Prove business model |

### 9.2 Why Pragmatic Layered for Question 1

| Factor | Assessment | Pattern Choice |
|--------|------------|----------------|
| **Team familiarity** | Layered is most common | Layered base |
| **Need to swap database?** | Unlikely but test-friendly | Interface for repos |
| **Need to swap payment provider?** | Possible | Interface for payment |
| **Rich domain logic?** | Moderate (order states, totals) | Some DDD patterns |
| **Time to market** | Critical | Less ceremony |
| **Future extraction possible** | Question 2/3 may require | Interfaces ready |

### 9.3 What We Use

| Component | Approach |
|-----------|----------|
| **Overall Structure** | Pragmatic Layered (4 layers) |
| **Repository Access** | Interface in Application, Implementation in Infrastructure |
| **External APIs** | Interface in Application, Implementation in Infrastructure |
| **Domain Entities** | Rich domain model where valuable (Order has business methods) |
| **Simple Entities** | Can be data containers (MenuItem is mostly data) |
| **Cross-Module Calls** | Through Application layer interfaces |

### 9.4 Example Structure for Order Module

```
modules/
└── order/
    ├── presentation/
    │   └── OrderController.ts
    │
    ├── application/
    │   ├── services/
    │   │   └── OrderService.ts
    │   ├── dtos/
    │   │   ├── PlaceOrderRequest.ts
    │   │   └── OrderResponse.ts
    │   └── ports/
    │       ├── OrderRepository.ts           # Interface
    │       └── PaymentGateway.ts            # Interface
    │
    ├── domain/
    │   ├── Order.ts                         # Entity with business logic
    │   ├── OrderItem.ts                     # Entity
    │   ├── OrderStatus.ts                   # Enum/Value Object
    │   └── Money.ts                         # Value Object
    │
    └── infrastructure/
        ├── PostgresOrderRepository.ts       # Implements OrderRepository
        └── persistence/
            └── OrderMapper.ts               # DB row <-> Domain object
```

### 9.5 Why This Pairs with Modular Monolith

1. **Each Module Has Same Structure**
   - Consistent across all 8 modules
   - Developers learn once, apply everywhere

2. **Module Boundaries Reinforced**
   - Application layer defines what module exposes
   - Infrastructure is internal to module

3. **Shared Database, Owned Tables**
   - Each module's infrastructure accesses only its tables
   - Cross-module data goes through application interfaces

4. **Prepared for Future**
   - If we extract Order module as a service (Question 3), interfaces exist
   - Replace PostgresOrderRepository with NetworkOrderRepository

---

## 10. References

### 10.1 Books

| Book | Author | Year | Notes |
|------|--------|------|-------|
| **Clean Architecture** | Robert C. Martin | 2017 | Canonical Clean Architecture reference |
| **Domain-Driven Design** | Eric Evans | 2003 | Original DDD book |
| **Implementing Domain-Driven Design** | Vaughn Vernon | 2013 | Practical DDD |
| **Patterns of Enterprise Application Architecture** | Martin Fowler | 2002 | Classic patterns including Layered |
| **Fundamentals of Software Architecture** | Mark Richards, Neal Ford | 2020 | Modern architecture overview |
| **Get Your Hands Dirty on Clean Architecture** | Tom Hombergs | 2019 | Practical Clean Architecture in Java |

### 10.2 Articles and Blog Posts

| Article | Author/Source | Link |
|---------|---------------|------|
| **The Clean Architecture** | Robert C. Martin (Uncle Bob) | https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html |
| **Hexagonal Architecture** | Alistair Cockburn | https://alistair.cockburn.us/hexagonal-architecture/ |
| **DDD, Hexagonal, Onion, Clean, CQRS... How I put it all together** | Herberto Graca | https://herbertograca.com/2017/11/16/explicit-architecture-01-ddd-hexagonal-onion-clean-cqrs-how-i-put-it-all-together/ |
| **Vertical Slice Architecture** | Jimmy Bogard | https://www.jimmybogard.com/vertical-slice-architecture/ |
| **Screaming Architecture** | Robert C. Martin | https://blog.cleancoder.com/uncle-bob/2011/09/30/Screaming-Architecture.html |
| **Modular Monolith: Domain-Centric Design** | Kamil Grzybek | https://www.kamilgrzybek.com/design/modular-monolith-domain-centric-design/ |
| **The Anemic Domain Model** | Martin Fowler | https://martinfowler.com/bliki/AnemicDomainModel.html |

### 10.3 Conference Talks and Videos

| Talk | Speaker | Event/Platform | Link |
|------|---------|----------------|------|
| **Clean Architecture and Design** | Robert C. Martin | NDC Conference | https://www.youtube.com/watch?v=Nsjsiz2A9mg |
| **Implementing Domain-Driven Design** | Vaughn Vernon | Various | https://www.youtube.com/watch?v=pmfq2Mze1qc |
| **Hexagonal Architecture** | Alistair Cockburn | Various | https://www.youtube.com/watch?v=AOIWUPjal60 |
| **Vertical Slice Architecture** | Jimmy Bogard | NDC Sydney | https://www.youtube.com/watch?v=SUiWfhAhgQw |
| **Clean Architecture with ASP.NET Core** | Jason Taylor | NDC Sydney | https://www.youtube.com/watch?v=dK4Yb6-LxAk |
| **SOLID Principles of Object Oriented Design** | Robert C. Martin | Pluralsight | https://www.pluralsight.com/courses/principles-oo-design |

### 10.4 Example Repositories

| Repository | Description | Link |
|------------|-------------|------|
| **Clean Architecture Solution Template** | .NET Clean Architecture | https://github.com/jasontaylordev/CleanArchitecture |
| **Modular Monolith with DDD** | .NET example | https://github.com/kgrzybek/modular-monolith-with-ddd |
| **Node.js Clean Architecture** | TypeScript example | https://github.com/jbuget/nodejs-clean-architecture-app |
| **Buckpal** | Java Clean Architecture | https://github.com/thombergs/buckpal |
| **MediatR** | CQRS/Vertical Slice library | https://github.com/jbogard/MediatR |

### 10.5 Additional Resources

| Resource | Type | Link |
|----------|------|------|
| **SOLID Principles** | Concept | https://en.wikipedia.org/wiki/SOLID |
| **Dependency Inversion Principle** | Concept | https://en.wikipedia.org/wiki/Dependency_inversion_principle |
| **Ports and Adapters Pattern** | Pattern | https://softwarecampament.wordpress.com/portsadapters/ |
| **ArchUnit** | Java architecture testing | https://www.archunit.org/ |
| **NetArchTest** | .NET architecture testing | https://github.com/BenMorris/NetArchTest |

---

## Document Revision History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Initial | Created for Question 1 architecture design |
