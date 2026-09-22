# Deployment Topology Research: Monolith, Modular Monolith, and Microservices

## Document Purpose

This document provides in-depth research on deployment topology patterns - specifically the Monolith, Modular Monolith, and Microservices architectures. While written in the context of our food delivery platform (Question 1), this serves as a foundational research document explaining what these patterns are, their characteristics, trade-offs, and when to use each.

---

## Table of Contents

1. [What is Deployment Topology?](#1-what-is-deployment-topology)
2. [The Traditional Monolith](#2-the-traditional-monolith)
3. [Microservices Architecture](#3-microservices-architecture)
4. [The Modular Monolith](#4-the-modular-monolith)
5. [Comparison Matrix](#5-comparison-matrix)
6. [Decision Framework](#6-decision-framework)
7. [Common Misconceptions](#7-common-misconceptions)
8. [Industry Perspectives](#8-industry-perspectives)
9. [Application to Question 1](#9-application-to-question-1)
10. [References](#10-references)

---

## 1. What is Deployment Topology?

**Deployment topology** refers to how an application is structured, packaged, and deployed as one or more executable units. It answers the question: *"How many separately deployable pieces does our system consist of?"*

This is distinct from:
- **Internal architecture** (how code is organized within a deployable unit)
- **Communication patterns** (how components talk to each other)
- **Infrastructure** (where and how the application runs)

### The Spectrum of Deployment Topologies

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      DEPLOYMENT TOPOLOGY SPECTRUM                           │
└─────────────────────────────────────────────────────────────────────────────┘

    SINGLE UNIT                                              MANY UNITS
         │                                                        │
         ▼                                                        ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────────────────┐
│   Traditional   │    │    Modular      │    │       Microservices         │
│    Monolith     │    │    Monolith     │    │                             │
│                 │    │                 │    │                             │
│  One codebase   │    │  One codebase   │    │  Many codebases             │
│  One deployment │    │  One deployment │    │  Many deployments           │
│  Tightly coupled│    │  Loosely coupled│    │  Independently deployable   │
│  internally     │    │  internally     │    │                             │
└─────────────────┘    └─────────────────┘    └─────────────────────────────┘
         │                     │                            │
         │                     │                            │
         ▼                     ▼                            ▼
    Simplest to            Middle ground              Most flexible but
    deploy and             Best of both?              most complex
    operate
```

---

## 2. The Traditional Monolith

### 2.1 Definition

A **monolith** is an application where all functionality is packaged and deployed as a single unit. The entire application - user interface, business logic, data access, and all features - exists in one codebase and runs as one process.

The term "monolith" comes from geology, referring to a single massive stone. In software, it describes a system that is "all one piece."

### 2.2 Characteristics

| Characteristic | Description |
|----------------|-------------|
| **Single Codebase** | All code lives in one repository |
| **Single Deployment** | The entire application is deployed at once |
| **Single Process** | Runs as one operating system process (typically) |
| **Shared Memory** | Components communicate via in-process calls |
| **Shared Database** | All components use the same database |
| **Single Technology Stack** | Usually one programming language/framework |

### 2.3 Visual Representation

```
┌─────────────────────────────────────────────────────────────────────┐
│                    TRADITIONAL MONOLITH                             │
│                                                                     │
│   ┌─────────────────────────────────────────────────────────────┐   │
│   │                      User Interface                          │   │
│   └─────────────────────────────────────────────────────────────┘   │
│                              │                                      │
│   ┌─────────────────────────────────────────────────────────────┐   │
│   │                                                              │   │
│   │   ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │   │
│   │   │ Feature  │  │ Feature  │  │ Feature  │  │ Feature  │    │   │
│   │   │    A     │◀─▶│    B     │◀─▶│    C     │◀─▶│    D     │    │   │
│   │   │          │  │          │  │          │  │          │    │   │
│   │   └──────────┘  └──────────┘  └──────────┘  └──────────┘    │   │
│   │          │            │            │            │           │   │
│   │          └────────────┼────────────┼────────────┘           │   │
│   │                       │            │                        │   │
│   │                       ▼            ▼                        │   │
│   │              Any feature can call any other                 │   │
│   │              No enforced boundaries                         │   │
│   │                                                              │   │
│   └─────────────────────────────────────────────────────────────┘   │
│                              │                                      │
│   ┌─────────────────────────────────────────────────────────────┐   │
│   │                    Shared Database                           │   │
│   └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
                               │
                               ▼
                    Single Deployment Unit
```

### 2.4 Advantages

1. **Simplicity of Development**
   - Single codebase to understand
   - Easy to set up development environment
   - IDE support for navigation and refactoring
   - No network calls between components

2. **Simplicity of Deployment**
   - One artifact to build
   - One thing to deploy
   - No orchestration needed
   - Atomic deployments (all or nothing)

3. **Simplicity of Testing**
   - End-to-end tests run against one application
   - No need to mock external services
   - Integration testing is straightforward

4. **Performance**
   - In-process calls are fast (nanoseconds)
   - No network latency between components
   - No serialization/deserialization overhead
   - Shared memory for data passing

5. **Consistency**
   - Single database ensures strong consistency
   - ACID transactions across features
   - No distributed transaction complexity

6. **Debugging**
   - Single process to attach debugger to
   - Full stack traces
   - No distributed tracing needed

### 2.5 Disadvantages

1. **Scaling Limitations**
   - Must scale entire application
   - Cannot scale components independently
   - Resource-intensive features affect others

2. **Deployment Risk**
   - Any change requires full redeployment
   - Change in Feature A risks breaking Feature B
   - Longer deployment cycles as application grows

3. **Technology Lock-in**
   - Single language/framework for everything
   - Difficult to adopt new technologies
   - Version upgrades affect entire system

4. **Team Coordination**
   - Large teams step on each other's toes
   - Merge conflicts increase
   - Ownership boundaries unclear

5. **The "Big Ball of Mud" Risk**
   - Without discipline, components become tangled
   - Dependencies become circular
   - Changes ripple unpredictably
   - Martin Fowler calls this "monolith degradation"

### 2.6 The Big Ball of Mud Problem

The term "Big Ball of Mud" was coined by Brian Foote and Joseph Yoder in 1997 to describe systems that have no discernible architecture:

> "A Big Ball of Mud is a haphazardly structured, sprawling, sloppy, duct-tape-and-baling-wire, spaghetti-code jungle."

This happens to monoliths when:
- There are no enforced boundaries between features
- Developers take shortcuts due to deadline pressure
- No clear ownership of code sections
- Quick fixes accumulate over time

```
┌─────────────────────────────────────────────────────────────────────┐
│                    BIG BALL OF MUD                                  │
│                                                                     │
│   ┌──────┐     ┌──────┐     ┌──────┐     ┌──────┐                  │
│   │  A   │◀───▶│  B   │◀───▶│  C   │◀───▶│  D   │                  │
│   └──┬───┘     └──┬───┘     └──┬───┘     └──┬───┘                  │
│      │    ╲       │    ╲       │    ╲       │                      │
│      │     ╲      │     ╲      │     ╲      │                      │
│      ▼      ╲     ▼      ╲     ▼      ╲     ▼                      │
│   ┌──────┐   ╲ ┌──────┐   ╲ ┌──────┐   ╲ ┌──────┐                  │
│   │  E   │◀──╳▶│  F   │◀──╳▶│  G   │◀──╳▶│  H   │                  │
│   └──┬───┘  ╱  └──┬───┘  ╱  └──┬───┘  ╱  └──┬───┘                  │
│      │     ╱      │     ╱      │     ╱      │                      │
│      │    ╱       │    ╱       │    ╱       │                      │
│      ▼   ╱        ▼   ╱        ▼   ╱        ▼                      │
│   ┌──────┐     ┌──────┐     ┌──────┐     ┌──────┐                  │
│   │  I   │◀───▶│  J   │◀───▶│  K   │◀───▶│  L   │                  │
│   └──────┘     └──────┘     └──────┘     └──────┘                  │
│                                                                     │
│   Everything depends on everything                                  │
│   Change one thing, break ten others                               │
│   Nobody understands the full system                               │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 3. Microservices Architecture

### 3.1 Definition

**Microservices** is an architectural style that structures an application as a collection of small, autonomous services, each running in its own process, communicating over a network, and deployable independently.

The term was popularized around 2011-2014 by practitioners at Netflix, Amazon, and ThoughtWorks, with James Lewis and Martin Fowler providing one of the canonical definitions.

### 3.2 Characteristics

| Characteristic | Description |
|----------------|-------------|
| **Multiple Services** | Application split into many small services |
| **Independent Deployment** | Each service can be deployed separately |
| **Own Process** | Each service runs as its own process |
| **Network Communication** | Services communicate over HTTP, gRPC, messaging |
| **Database Per Service** | Each service owns its data (ideally) |
| **Polyglot** | Different services can use different technologies |
| **Team Ownership** | Each service owned by a small team |

### 3.3 Visual Representation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       MICROSERVICES ARCHITECTURE                            │
└─────────────────────────────────────────────────────────────────────────────┘

                              API Gateway
                                  │
            ┌─────────────────────┼─────────────────────┐
            │                     │                     │
            ▼                     ▼                     ▼
    ┌───────────────┐     ┌───────────────┐     ┌───────────────┐
    │   Service A   │     │   Service B   │     │   Service C   │
    │   (Node.js)   │────▶│   (Python)    │────▶│   (Java)      │
    │               │     │               │     │               │
    └───────┬───────┘     └───────┬───────┘     └───────┬───────┘
            │                     │                     │
            ▼                     ▼                     ▼
    ┌───────────────┐     ┌───────────────┐     ┌───────────────┐
    │   Database A  │     │   Database B  │     │   Database C  │
    │  (PostgreSQL) │     │   (MongoDB)   │     │   (Redis)     │
    └───────────────┘     └───────────────┘     └───────────────┘

    ┌───────────────────────────────────────────────────────────────────────┐
    │                                                                       │
    │   Each service:                                                       │
    │   - Deployed independently                                            │
    │   - Scaled independently                                              │
    │   - Has its own database                                              │
    │   - Can use different technology                                      │
    │   - Owned by a specific team                                          │
    │                                                                       │
    └───────────────────────────────────────────────────────────────────────┘
```

### 3.4 Key Principles (from Sam Newman)

Sam Newman, in "Building Microservices," outlines these key characteristics:

1. **Modeled Around Business Domain**
   - Services align with business capabilities
   - Not technical layers (UI service, data service)
   - "A service should be able to be rewritten in 2 weeks"

2. **Culture of Automation**
   - CI/CD pipelines for each service
   - Automated testing
   - Infrastructure as code

3. **Hide Implementation Details**
   - Services expose APIs, hide internals
   - Database is private to service
   - No shared database access

4. **Decentralize All the Things**
   - Decentralized data management
   - Decentralized governance
   - Each team makes technology decisions

5. **Deploy Independently**
   - Change to one service doesn't require others
   - No coordinated deployments
   - Feature flags for gradual rollout

6. **Isolate Failure**
   - One service failing shouldn't cascade
   - Circuit breakers, bulkheads
   - Graceful degradation

7. **Highly Observable**
   - Distributed tracing
   - Centralized logging
   - Health checks
   - Metrics and alerting

### 3.5 Advantages

1. **Independent Deployment**
   - Deploy services without affecting others
   - Faster release cycles
   - Lower deployment risk per change
   - Canary deployments possible

2. **Independent Scaling**
   - Scale only the services that need it
   - Different scaling strategies per service
   - Cost optimization

3. **Technology Flexibility**
   - Best tool for each job
   - Easier to adopt new technologies
   - Can rewrite service in different language

4. **Team Autonomy**
   - Small teams own their service
   - Full-stack ownership
   - Amazon's "Two Pizza Teams"
   - Reduced coordination overhead

5. **Fault Isolation**
   - Failure in one service is contained
   - Other services continue working
   - Easier to identify problem source

6. **Organizational Scaling**
   - Can add teams without everyone blocking each other
   - Conway's Law working in your favor
   - Clear ownership boundaries

### 3.6 Disadvantages

1. **Distributed System Complexity**
   - Network is unreliable
   - Latency between services
   - Partial failures
   - The "8 fallacies of distributed computing"

2. **Operational Overhead**
   - Many services to deploy and monitor
   - Need container orchestration (Kubernetes)
   - Need service mesh, API gateway
   - Need distributed tracing

3. **Data Consistency Challenges**
   - No cross-service transactions
   - Eventually consistent
   - Saga patterns required
   - Data duplication

4. **Testing Complexity**
   - Integration tests require running multiple services
   - Contract testing needed
   - End-to-end tests are slow and flaky

5. **Debugging Difficulty**
   - Request spans multiple services
   - Need distributed tracing tools
   - Logs scattered across services
   - Correlation IDs required

6. **Network Overhead**
   - Service-to-service calls have latency
   - Serialization/deserialization cost
   - Need for resilience patterns

7. **Initial Development Slower**
   - More infrastructure to set up
   - More boilerplate per service
   - Learning curve for team

### 3.7 The 8 Fallacies of Distributed Computing

Peter Deutsch (Sun Microsystems) identified these false assumptions developers make:

1. The network is reliable
2. Latency is zero
3. Bandwidth is infinite
4. The network is secure
5. Topology doesn't change
6. There is one administrator
7. Transport cost is zero
8. The network is homogeneous

Each fallacy leads to bugs and outages in distributed systems. Microservices must account for all of these.

### 3.8 Required Infrastructure

A mature microservices architecture typically requires:

| Component | Purpose |
|-----------|---------|
| **Container Runtime** | Docker, containerd |
| **Orchestration** | Kubernetes, ECS, Docker Swarm |
| **Service Discovery** | Consul, Kubernetes DNS, Eureka |
| **API Gateway** | Kong, Ambassador, AWS API Gateway |
| **Load Balancer** | HAProxy, NGINX, cloud load balancers |
| **Service Mesh** | Istio, Linkerd (optional but common) |
| **Distributed Tracing** | Jaeger, Zipkin, AWS X-Ray |
| **Centralized Logging** | ELK Stack, Splunk, CloudWatch |
| **Monitoring** | Prometheus, Grafana, Datadog |
| **CI/CD per Service** | Jenkins, GitLab CI, GitHub Actions |
| **Secret Management** | Vault, AWS Secrets Manager |
| **Message Broker** | Kafka, RabbitMQ, SQS (for async) |

---

## 4. The Modular Monolith

### 4.1 Definition

A **Modular Monolith** is a single deployable unit (like a traditional monolith) but with a well-defined internal structure where the application is divided into independent modules with explicit boundaries and clear interfaces.

It aims to get the development simplicity of a monolith while maintaining the clean separation you'd find in microservices - just without the distributed system complexity.

Simon Brown describes it as: "If you can't build a well-structured monolith, what makes you think you can build a well-structured set of microservices?"

### 4.2 Characteristics

| Characteristic | Description |
|----------------|-------------|
| **Single Deployment** | Still deployed as one unit |
| **Module Boundaries** | Clear separation between modules |
| **Defined Interfaces** | Modules communicate through APIs |
| **Encapsulated Data** | Each module owns its data (tables) |
| **In-Process Calls** | Communication via method calls, not network |
| **Shared Database** | One database, but with ownership rules |
| **Enforceable Boundaries** | Can use tooling to prevent violations |

### 4.3 Visual Representation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         MODULAR MONOLITH                                    │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                         API Layer                                    │   │
│   └───────────────────────────────────────────────────────────────────── │   │
│                                    │                                        │
│   ┌───────────────┬───────────────┬───────────────┬───────────────┐        │
│   │               │               │               │               │        │
│   │   ┌───────┐   │   ┌───────┐   │   ┌───────┐   │   ┌───────┐   │        │
│   │   │Module │   │   │Module │   │   │Module │   │   │Module │   │        │
│   │   │   A   │   │   │   B   │   │   │   C   │   │   │   D   │   │        │
│   │   │       │   │   │       │   │   │       │   │   │       │   │        │
│   │   │ ───── │   │   │ ───── │   │   │ ───── │   │   │ ───── │   │        │
│   │   │Public │   │   │Public │   │   │Public │   │   │Public │   │        │
│   │   │  API  │   │   │  API  │   │   │  API  │   │   │  API  │   │        │
│   │   └───┬───┘   │   └───┬───┘   │   └───┬───┘   │   └───┬───┘   │        │
│   │       │       │       │       │       │       │       │       │        │
│   │  ─────┼─────  │  ─────┼─────  │  ─────┼─────  │  ─────┼─────  │        │
│   │       │       │       │       │       │       │       │       │        │
│   │   ┌───┴───┐   │   ┌───┴───┐   │   ┌───┴───┐   │   ┌───┴───┐   │        │
│   │   │Private│   │   │Private│   │   │Private│   │   │Private│   │        │
│   │   │ Logic │   │   │ Logic │   │   │ Logic │   │   │ Logic │   │        │
│   │   │       │   │   │       │   │   │       │   │   │       │   │        │
│   │   └───────┘   │   └───────┘   │   └───────┘   │   └───────┘   │        │
│   │               │               │               │               │        │
│   │   Module A    │   Module B    │   Module C    │   Module D    │        │
│   │   Tables      │   Tables      │   Tables      │   Tables      │        │
│   │               │               │               │               │        │
│   └───────────────┴───────────────┴───────────────┴───────────────┘        │
│                                    │                                        │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                    Shared Database (PostgreSQL)                      │   │
│   │   ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐             │   │
│   │   │Tables A │   │Tables B │   │Tables C │   │Tables D │             │   │
│   │   │(owned)  │   │(owned)  │   │(owned)  │   │(owned)  │             │   │
│   │   └─────────┘   └─────────┘   └─────────┘   └─────────┘             │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                     │
                                     ▼
                          Single Deployment Unit
                          But with clear internal structure
```

### 4.4 Module Boundaries - The Key Concept

The defining feature of a modular monolith is **enforced module boundaries**. Each module:

1. **Exposes a Public Interface**
   - Other modules can only call through this interface
   - Typically implemented as interfaces/abstract classes
   - Can be as simple as a facade class

2. **Hides Internal Implementation**
   - Internal classes are not accessible to other modules
   - Database tables "owned" by the module
   - Implementation can change without affecting others

3. **Owns Its Data**
   - Each module owns specific database tables
   - Other modules cannot directly query these tables
   - Must go through the owning module's API

```
Module A wants data from Module B:

WRONG (coupling):
┌──────────────┐                    ┌──────────────┐
│   Module A   │                    │   Module B   │
│              │─────SELECT────────▶│   Tables     │
└──────────────┘     query          └──────────────┘

RIGHT (encapsulation):
┌──────────────┐                    ┌──────────────┐
│   Module A   │─────GetData()─────▶│   Module B   │
│              │◀────Return────────│   (API)      │
│              │                    │      │       │
└──────────────┘                    │      ▼       │
                                    │   Tables     │
                                    └──────────────┘
```

### 4.5 Enforcing Boundaries

Unlike microservices where network boundaries enforce separation, modular monoliths need other mechanisms:

| Method | Description |
|--------|-------------|
| **Separate Packages/Namespaces** | Group module code in distinct packages |
| **Access Modifiers** | Use internal/private visibility |
| **Architecture Tests** | ArchUnit (Java), NetArchTest (.NET) |
| **Build-Time Checks** | Fail build on boundary violations |
| **Separate Projects** | Each module is a project/assembly |
| **Code Review** | Manual enforcement during review |

Example using ArchUnit (Java):
```java
@ArchTest
static final ArchRule orderModuleDoesNotDependOnPaymentInternals =
    noClasses()
        .that().resideInAPackage("..order..")
        .should().dependOnClassesThat()
        .resideInAPackage("..payment.internal..");
```

### 4.6 Advantages

1. **Simplicity of a Monolith**
   - Single deployment
   - No network calls between modules
   - Easy to debug
   - ACID transactions when needed

2. **Structure of Microservices**
   - Clear boundaries
   - Team ownership possible
   - Independent module development
   - Prepared for extraction if needed

3. **Lower Operational Complexity**
   - No container orchestration needed
   - No service discovery
   - No distributed tracing
   - Standard monitoring

4. **Easier Refactoring**
   - IDE support for refactoring across modules
   - Can easily move code between modules
   - Full visibility of dependencies

5. **Path to Microservices**
   - Can extract modules later if needed
   - Already have boundaries defined
   - Evolutionary architecture

6. **Strong Consistency When Needed**
   - Can use database transactions across modules
   - No saga patterns needed
   - ACID guarantees available

### 4.7 Disadvantages

1. **Single Point of Deployment**
   - Still must deploy everything together
   - Cannot do independent service deployments
   - Larger deployment artifact

2. **Single Point of Failure**
   - Bug can bring down entire application
   - Memory leak affects all modules
   - No fault isolation

3. **Technology Lock-in**
   - All modules use same language/framework
   - Version upgrades affect everything
   - Cannot use "best tool for job"

4. **Scaling Limitations**
   - Must scale entire application
   - Resource-heavy module affects others
   - Cannot scale modules independently

5. **Discipline Required**
   - Boundaries must be actively enforced
   - Easy to take shortcuts
   - Without tools, boundaries degrade

6. **Still a Monolith**
   - Very large monoliths still problematic
   - Build times can grow
   - IDE performance can degrade

### 4.8 When to Use a Modular Monolith

Based on industry guidance, a modular monolith is recommended when:

| Condition | Why Modular Monolith Works |
|-----------|---------------------------|
| **Small team** (< 10-15 developers) | No need for service boundaries to separate teams |
| **New/Uncertain domain** | Boundaries may shift; monolith makes this easy |
| **Limited ops capability** | Don't have DevOps team for microservices |
| **Need strong consistency** | ACID transactions simpler |
| **Speed to market** | Faster initial development |
| **Unclear boundaries** | Can refine boundaries before extracting |

---

## 5. Comparison Matrix

### 5.1 Feature Comparison

| Feature | Traditional Monolith | Modular Monolith | Microservices |
|---------|---------------------|------------------|---------------|
| **Deployment Units** | 1 | 1 | Many |
| **Codebase** | 1 | 1 | Many |
| **Internal Boundaries** | None/Weak | Strong | N/A (external) |
| **Module Communication** | Any method call | Interface calls | Network calls |
| **Data Ownership** | Unclear | Clear (tables) | Clear (database) |
| **Database** | Shared | Shared (owned tables) | Per-service |
| **Scalability** | Whole app | Whole app | Per-service |
| **Technology Diversity** | No | No | Yes |
| **Team Autonomy** | Low | Medium | High |
| **Operational Complexity** | Low | Low | High |
| **Initial Development Speed** | Fast | Fast | Slow |
| **Long-term Maintainability** | Degrades | Maintained | Maintained |
| **Fault Isolation** | None | None | Good |
| **Consistency Model** | Strong (ACID) | Strong (ACID) | Eventually consistent |

### 5.2 Infrastructure Requirements

| Requirement | Traditional Monolith | Modular Monolith | Microservices |
|-------------|---------------------|------------------|---------------|
| Container orchestration | Optional | Optional | Essential |
| Service discovery | No | No | Yes |
| API gateway | Optional | Optional | Yes |
| Distributed tracing | No | No | Yes |
| Service mesh | No | No | Often |
| Multiple CI/CD pipelines | No | No | Yes |
| Log aggregation | Simple | Simple | Essential |
| DevOps team | Optional | Optional | Essential |

### 5.3 Team Size Guidance

| Team Size | Recommendation | Reasoning |
|-----------|----------------|-----------|
| 1-5 developers | Traditional or Modular Monolith | Small team, no need for service isolation |
| 5-15 developers | Modular Monolith | Boundaries help, but not worth distribution costs |
| 15-50 developers | Modular Monolith or Microservices | Depends on domain clarity and ops capability |
| 50+ developers | Microservices | Team autonomy becomes critical |

---

## 6. Decision Framework

### 6.1 Start with a Monolith (Martin Fowler's Advice)

Martin Fowler's famous advice in "Monolith First":

> "Almost all the successful microservice stories have started with a monolith that got too big and was broken up."

His reasoning:
1. You don't know service boundaries upfront
2. Microservices only make sense when you have well-defined boundaries
3. Starting with microservices is "premature decomposition"
4. It's easier to split a well-structured monolith than to merge poorly defined services

### 6.2 Sam Newman's Perspective

Sam Newman in "Building Microservices" (2nd edition, 2021):

> "Microservices are not the goal. They're a tool to help you achieve a goal. If you don't have a problem that microservices solve, you don't need microservices."

He identifies these as valid reasons to adopt microservices:
- Need independent deployability
- Need independent scalability
- Need technology flexibility
- Need team autonomy at scale
- Need fault isolation

### 6.3 Decision Flowchart

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DEPLOYMENT TOPOLOGY DECISION                             │
└─────────────────────────────────────────────────────────────────────────────┘

                              START
                                │
                                ▼
                    ┌───────────────────────┐
                    │ Do you have a DevOps  │
                    │ team or capability?   │
                    └───────────┬───────────┘
                                │
               ┌────────────────┼────────────────┐
               │ No             │                │ Yes
               ▼                │                ▼
    ┌─────────────────┐         │    ┌─────────────────┐
    │    MONOLITH     │         │    │ How many        │
    │    (Modular)    │         │    │ developers?     │
    └─────────────────┘         │    └───────┬─────────┘
                                │            │
                                │   ┌────────┼────────┐
                                │   │        │        │
                                │ <15      15-50    >50
                                │   │        │        │
                                ▼   ▼        ▼        ▼
                    ┌─────────────────┐  ┌─────────────────┐
                    │    MONOLITH     │  │ Do you need     │
                    │    (Modular)    │  │ independent     │
                    └─────────────────┘  │ scaling/deploy? │
                                         └───────┬─────────┘
                                                 │
                                    ┌────────────┼────────────┐
                                    │ No         │            │ Yes
                                    ▼            │            ▼
                        ┌─────────────────┐      │ ┌─────────────────┐
                        │    MONOLITH     │      │ │ MICROSERVICES   │
                        │    (Modular)    │      │ └─────────────────┘
                        └─────────────────┘      │
                                                 │
                                                 │ Not sure?
                                                 ▼
                                    ┌─────────────────┐
                                    │ Start with      │
                                    │ Modular Monolith│
                                    │ Extract later   │
                                    └─────────────────┘
```

### 6.4 Questions to Ask

Before choosing microservices, ask:

1. **Do we need independent deployment?**
   - If no: Monolith is fine
   - If yes: Consider microservices

2. **Do we need independent scaling?**
   - If workload is uniform: Monolith can scale
   - If one component needs 10x: Microservices help

3. **Do we have well-defined domain boundaries?**
   - If uncertain: Start with monolith, learn the domain
   - If clear: Either works

4. **What's our team size and structure?**
   - Small team: Monolith
   - Large organization: Microservices enable autonomy

5. **What's our operational maturity?**
   - No DevOps: Monolith
   - Strong ops: Microservices possible

6. **What consistency model do we need?**
   - Strong consistency critical: Monolith easier
   - Eventual consistency OK: Either works

---

## 7. Common Misconceptions

### 7.1 "Monoliths Don't Scale"

**Reality:** Monoliths can scale horizontally. Run multiple instances behind a load balancer. Many large companies (Shopify, Basecamp, Etsy at one point) run scaled monoliths.

The limitation is *independent* scaling - you can't scale just one part.

### 7.2 "Microservices Are Always Better"

**Reality:** Microservices introduce significant complexity. For many applications, they're worse:
- More infrastructure to manage
- Distributed system problems
- Slower development for small teams
- Operational overhead

They're better only when you have the problems they solve.

### 7.3 "Monolith Means Spaghetti Code"

**Reality:** A monolith can be well-structured (modular monolith) or poorly structured (big ball of mud). The deployment topology doesn't determine code quality.

Similarly, microservices can become a "distributed big ball of mud" if poorly designed.

### 7.4 "Microservices Mean Better Code"

**Reality:** Drawing service boundaries doesn't automatically create good code. You can have:
- Poorly designed microservices
- Tight coupling between services
- Distributed monolith (worst of both worlds)

Good design principles matter regardless of topology.

### 7.5 "We're Too Small for Good Architecture"

**Reality:** Good architecture (modular design, clear boundaries) is valuable at any size. A modular monolith gives structure without complexity.

The question isn't "are we big enough for good architecture" but "which architecture fits our constraints."

---

## 8. Industry Perspectives

### 8.1 ThoughtWorks Technology Radar

ThoughtWorks has consistently advised caution with microservices:

> "Microservices envy is a real thing... Teams are adopting microservices architecture without understanding the complexity it brings." (2015)

> "We recommend starting with a modular monolith and only moving to microservices when the monolith becomes a clear bottleneck." (2020)

### 8.2 Amazon's Evolution

Amazon's evolution is often cited:
- Started as a monolith (early 2000s)
- Evolved to SOA (Service-Oriented Architecture)
- Eventually to microservices

Key point: They evolved when needed, didn't start with microservices.

### 8.3 Netflix's Reality

Netflix is famous for microservices, but:
- They have 2000+ engineers
- Dedicated platform teams
- Years of investment in tooling
- Solved specific problems at massive scale

What works for Netflix probably doesn't apply to most companies.

### 8.4 Shopify's Modular Monolith

Shopify chose to keep their modular monolith:

> "We've been investing in making our monolith more modular rather than splitting it up. We call this approach 'componentization.'" (2019)

They use clear module boundaries within a single deployable.

### 8.5 Basecamp/DHH's Perspective

David Heinemeier Hansson (creator of Ruby on Rails):

> "The Majestic Monolith can be a perfectly valid architectural choice for companies of even enormous scale... Microservices are overhyped and overused."

Basecamp runs a monolith serving millions of users.

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

### 9.2 Why Modular Monolith for Question 1

Given the research above, Modular Monolith is the clear choice:

| Factor | Assessment | Topology |
|--------|------------|----------|
| **Team size (3)** | Far below threshold for microservices | Monolith |
| **No DevOps** | Cannot manage microservices infrastructure | Monolith |
| **Limited budget** | Cannot afford complex infrastructure | Monolith |
| **Uncertain domain** | First time building this; boundaries may shift | Monolith |
| **Time to market** | Need to prove business model quickly | Monolith |
| **Future growth** | May need to scale later | Modular (not traditional) |

Why **modular** specifically:
- Maintains clean boundaries for future extraction
- Prevents "big ball of mud" degradation
- Makes it easier to bring on new developers
- Prepared for evolution in Question 2/3

### 9.3 What This Means for Our Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    QUESTION 1: MODULAR MONOLITH                             │
│                                                                             │
│   Single Application                                                        │
│   ├── Identity/Auth Module    (owns: User)                                 │
│   ├── Customer Module         (owns: CustomerProfile, CustomerAddress)     │
│   ├── Restaurant Module       (owns: RestaurantProfile)                    │
│   ├── Menu Module             (owns: MenuItem)                             │
│   ├── Order Module            (owns: Order, OrderItem, OrderStateHistory)  │
│   ├── Payment Module          (owns: Payment)                              │
│   ├── Delivery Module         (owns: DriverProfile, DriverShift, Delivery) │
│   └── Reporting Module        (owns: nothing - reads from others)          │
│                                                                             │
│   Single Database (PostgreSQL)                                              │
│   └── All tables, but ownership rules enforced                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 10. References

### 10.1 Books

| Book | Author | Year | Notes |
|------|--------|------|-------|
| **Building Microservices** (2nd Edition) | Sam Newman | 2021 | Canonical reference for microservices |
| **Monolith to Microservices** | Sam Newman | 2019 | Evolution patterns and migration |
| **Fundamentals of Software Architecture** | Mark Richards, Neal Ford | 2020 | Comprehensive architecture patterns |
| **Software Architecture: The Hard Parts** | Neal Ford et al. | 2021 | Modern architecture trade-offs |
| **Domain-Driven Design** | Eric Evans | 2003 | Foundational for bounded contexts |
| **Implementing Domain-Driven Design** | Vaughn Vernon | 2013 | Practical DDD with architecture |

### 10.2 Articles and Blog Posts

| Article | Author/Source | Link |
|---------|---------------|------|
| **Monolith First** | Martin Fowler | https://martinfowler.com/bliki/MonolithFirst.html |
| **Microservices** | Martin Fowler & James Lewis | https://martinfowler.com/articles/microservices.html |
| **Microservice Trade-Offs** | Martin Fowler | https://martinfowler.com/articles/microservice-trade-offs.html |
| **Modular Monolith: A Primer** | Kamil Grzybek | https://www.kamilgrzybek.com/design/modular-monolith-primer/ |
| **The Majestic Monolith** | DHH (Basecamp) | https://m.signalvnoise.com/the-majestic-monolith/ |
| **Shopify's Architecture** | Shopify Engineering | https://shopify.engineering/deconstructing-monolith-designing-software-maximizes-developer-productivity |
| **Big Ball of Mud** | Brian Foote, Joseph Yoder | http://www.laputan.org/mud/ |
| **The Fallacies of Distributed Computing** | Peter Deutsch | https://www.rfc-editor.org/ien/ien137.txt |

### 10.3 Conference Talks and Videos

| Talk | Speaker | Event/Platform | Link |
|------|---------|----------------|------|
| **Modular Monoliths** | Simon Brown | GOTO Conference | https://www.youtube.com/watch?v=5OjqD-ow8GE |
| **Building Microservices** | Sam Newman | GOTO Conference | https://www.youtube.com/watch?v=PFQnNFe27kU |
| **When To Use Microservices (And When Not To!)** | Sam Newman | InfoQ | https://www.youtube.com/watch?v=GBTdnfD6s5Q |
| **Microservices at Netflix Scale** | Various | Netflix Tech Blog | https://netflixtechblog.com/ |
| **Don't Build a Distributed Monolith** | Jonathan Tower | GOTO Conference | https://www.youtube.com/watch?v=p2GlRToY5HI |
| **Majestic Modular Monoliths** | Axel Fontaine | Devoxx | https://www.youtube.com/watch?v=BOvxJaklcr0 |
| **Shopify's Modular Monolith** | Kirsten Westeinde | RailsConf | https://www.youtube.com/watch?v=ISYKx8sa53g |

### 10.4 Tools Referenced

| Tool | Purpose | Link |
|------|---------|------|
| **ArchUnit** (Java) | Architecture test library | https://www.archunit.org/ |
| **NetArchTest** (.NET) | Architecture test library | https://github.com/BenMorris/NetArchTest |
| **Kubernetes** | Container orchestration | https://kubernetes.io/ |
| **Istio** | Service mesh | https://istio.io/ |
| **Jaeger** | Distributed tracing | https://www.jaegertracing.io/ |

---

## Document Revision History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Initial | Created for Question 1 architecture design |
