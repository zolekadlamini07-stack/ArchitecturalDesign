# Caching Strategies Research

## Document Purpose

This document provides research on caching strategies, specifically for scenarios where data changes in real-time (like menu item availability). The goal is to inform our decision for Question 2's restaurant browsing traffic problem.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Caching Fundamentals](#2-caching-fundamentals)
3. [Caching Patterns](#3-caching-patterns)
4. [Cache Invalidation Strategies](#4-cache-invalidation-strategies)
5. [Caching Technologies](#5-caching-technologies)
6. [Real-Time Data Caching Approaches](#6-real-time-data-caching-approaches)
7. [Trade-offs Analysis](#7-trade-offs-analysis)
8. [Application to Q2](#8-application-to-q2)
9. [References](#9-references)

---

## 1. The Problem We're Solving

**Symptom:** Restaurant browsing is receiving significantly more traffic than other parts of the system.

**Q2 Context:**
- 15 restaurants (expecting 50)
- Menu items have real-time availability (items go unavailable immediately when sold out)
- High read-to-write ratio for restaurant/menu data
- Database being hit for every browse request

**Goal:** Reduce database load while maintaining data freshness for real-time availability.

---

## 2. Caching Fundamentals

### 2.1 What is Caching?

Caching stores copies of data in a faster storage layer to serve future requests more quickly. Instead of computing or fetching data every time, we serve it from the cache.

### 2.2 Cache Hit vs Cache Miss

| Term | Definition |
|------|------------|
| **Cache Hit** | Requested data found in cache, served directly |
| **Cache Miss** | Data not in cache, must be fetched from source |
| **Hit Rate** | Percentage of requests served from cache |
| **Miss Penalty** | Time cost of a cache miss (fetch + store) |

### 2.3 The Cache Trade-off

```
                  ┌─────────────────────────────────────────┐
                  │           THE CACHING TRADE-OFF         │
                  └─────────────────────────────────────────┘

   PERFORMANCE                                    CONSISTENCY
       │                                              │
       │  Fast responses                  Fresh data  │
       │  Low latency                     Accurate    │
       │  Reduced load                    Up-to-date  │
       │                                              │
       └──────────────────┬───────────────────────────┘
                          │
                    CACHING DECISION
                          │
              ┌───────────┴───────────┐
              │                       │
       Cache longer            Cache shorter
       Higher hit rate         More fresh data
       Possibly stale          More DB hits
```

### 2.4 Cache Staleness

**Stale data** = data in cache that no longer matches the source.

For different data types, staleness tolerance varies:

| Data Type | Staleness Tolerance | Example |
|-----------|---------------------|---------|
| Static content | Hours/Days | Restaurant logo, description |
| Semi-static | Minutes | Menu items, prices |
| Dynamic | Seconds | Item availability |
| Real-time | Zero | Stock price, live scores |

---

## 3. Caching Patterns

### 3.1 Cache-Aside (Lazy Loading)

Application manages the cache explicitly.

```
READ:
┌─────────┐     1. Check cache     ┌─────────┐
│  App    │─────────────────────▶│  Cache  │
│         │◀────────────────────│         │
│         │    2a. HIT: Return   └─────────┘
│         │
│         │    2b. MISS:
│         │─────────────────────▶┌─────────┐
│         │◀────────────────────│   DB    │
│         │    3. Fetch data     └─────────┘
│         │
│         │─────────────────────▶┌─────────┐
│         │    4. Store in cache │  Cache  │
└─────────┘                      └─────────┘

WRITE:
┌─────────┐     1. Write to DB    ┌─────────┐
│  App    │─────────────────────▶│   DB    │
│         │                       └─────────┘
│         │     2. Invalidate     ┌─────────┐
│         │─────────────────────▶│  Cache  │
└─────────┘                       └─────────┘
```

**Pros:**
- Simple to implement
- Only caches data that's actually requested
- Cache failures don't block reads (fall back to DB)

**Cons:**
- First request always misses (cold start)
- Potential for stale data between write and invalidation
- Application responsible for cache logic

**Best for:** Data that's read frequently but updated occasionally.

---

### 3.2 Read-Through

Cache sits between application and database. Cache fetches from DB on miss.

```
READ:
┌─────────┐     1. Request      ┌─────────┐     2. Miss     ┌─────────┐
│  App    │────────────────────▶│  Cache  │────────────────▶│   DB    │
│         │◀────────────────────│ (proxy) │◀────────────────│         │
│         │     4. Return       └─────────┘     3. Fetch    └─────────┘
└─────────┘                          │
                                     │ (stores automatically)
```

**Pros:**
- Application code simpler (doesn't manage cache)
- Cache always has latest from DB (on miss)

**Cons:**
- Cache becomes a dependency (if it fails, reads fail)
- Still stale data possible

**Best for:** When you want transparent caching.

---

### 3.3 Write-Through

Every write goes through cache to database.

```
WRITE:
┌─────────┐     1. Write        ┌─────────┐     2. Write    ┌─────────┐
│  App    │────────────────────▶│  Cache  │────────────────▶│   DB    │
│         │◀────────────────────│         │◀────────────────│         │
│         │     4. Confirm      └─────────┘     3. Confirm  └─────────┘
└─────────┘
                    Cache and DB always in sync
```

**Pros:**
- Cache always has latest data
- No stale data (writes go through cache)

**Cons:**
- Higher write latency (two writes: cache + DB)
- Cache may contain data that's never read

**Best for:** When consistency is critical and writes are infrequent.

---

### 3.4 Write-Behind (Write-Back)

Write to cache immediately, async write to database.

```
WRITE:
┌─────────┐     1. Write        ┌─────────┐
│  App    │────────────────────▶│  Cache  │
│         │◀────────────────────│         │
│         │     2. Return fast  └────┬────┘
└─────────┘                          │
                                     │ 3. Async write (later)
                                     ▼
                               ┌─────────┐
                               │   DB    │
                               └─────────┘
```

**Pros:**
- Very fast writes (cache only)
- Batching possible (combine multiple writes)

**Cons:**
- Data loss risk if cache fails before DB write
- Complexity in ensuring eventual consistency
- Not suitable for critical data

**Best for:** High write throughput, non-critical data.

---

### 3.5 Refresh-Ahead

Proactively refresh cache before expiration.

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   Item TTL: 60 seconds                                         │
│   Refresh threshold: 80% (48 seconds)                          │
│                                                                 │
│   0s                    48s                    60s              │
│   │                      │                      │               │
│   │◀─── Normal serving ─▶│◀─ Trigger refresh ─▶│◀─ Expired     │
│   │                      │                      │               │
│   └──────────────────────┼──────────────────────┘               │
│                          │                                      │
│                   Background refresh                            │
│                   (no user waits for DB)                        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Pros:**
- Reduces cache misses for frequently accessed data
- Users never wait for DB fetch

**Cons:**
- More complex to implement
- May refresh data that's no longer needed
- Still stale during refresh window

**Best for:** Frequently accessed data with predictable access patterns.

---

## 4. Cache Invalidation Strategies

> "There are only two hard things in Computer Science: cache invalidation and naming things." — Phil Karlton

### 4.1 Time-To-Live (TTL)

Data expires after a fixed time.

```
┌─────────────────────────────────────────────────────────────────┐
│                      TTL EXPIRATION                             │
│                                                                 │
│   Write at T=0          TTL = 60s          Expired at T=60     │
│       │                     │                    │              │
│       ▼                     ▼                    ▼              │
│   ┌───────┐             ┌───────┐           ┌───────┐          │
│   │ Fresh │────────────▶│ Aging │──────────▶│ Stale │          │
│   └───────┘             └───────┘           └───────┘          │
│                                                 │               │
│                                          (evicted or           │
│                                           refreshed)           │
└─────────────────────────────────────────────────────────────────┘
```

**Pros:**
- Simple, predictable
- No coordination needed
- Works for any data

**Cons:**
- Stale data possible until TTL expires
- Fixed staleness window

**Best for:** Data that can tolerate known staleness window.

**TTL Guidelines:**

| Data Type | Suggested TTL | Reasoning |
|-----------|---------------|-----------|
| Static (logos, descriptions) | 24 hours | Rarely changes |
| Semi-static (prices) | 5-15 minutes | Changes occasionally |
| Dynamic (availability) | 30-60 seconds | Changes frequently |
| Highly volatile | 5-10 seconds or don't cache | Changes constantly |

---

### 4.2 Event-Driven Invalidation

Invalidate cache when data changes.

```
┌─────────────────────────────────────────────────────────────────┐
│                  EVENT-DRIVEN INVALIDATION                      │
└─────────────────────────────────────────────────────────────────┘

┌─────────┐                    ┌─────────────┐
│ Menu    │    1. Update       │   Event     │
│ Service │───────────────────▶│   Bus       │
│         │  "MenuItemUpdated" │             │
└─────────┘                    └──────┬──────┘
                                      │
                         2. Broadcast │
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
        ▼                             ▼                             ▼
┌───────────────┐           ┌───────────────┐           ┌───────────────┐
│ Cache Service │           │ Other Service │           │ Analytics     │
│               │           │               │           │               │
│ 3. Invalidate │           │ (ignore)      │           │ (log change)  │
│    cache key  │           │               │           │               │
└───────────────┘           └───────────────┘           └───────────────┘
```

**Pros:**
- Cache invalidated immediately on change
- No stale data window
- Decoupled (cache doesn't know about writers)

**Cons:**
- Requires event infrastructure (message queue)
- More complex to implement
- Event delivery not guaranteed (unless using durable queue)

**Best for:** Real-time data where staleness is not acceptable.

---

### 4.3 Write-Through Invalidation

Invalidate/update cache as part of write operation.

```
┌─────────┐     1. Update Item    ┌─────────┐
│  App    │──────────────────────▶│   DB    │
│         │                       └─────────┘
│         │     2. Invalidate     ┌─────────┐
│         │──────────────────────▶│  Cache  │
│         │     (same transaction)└─────────┘
└─────────┘
```

**Pros:**
- Strong consistency
- Simple mental model

**Cons:**
- Couples write path to cache
- If cache invalidation fails, need rollback strategy
- All writers must know about cache

**Best for:** When all writes go through one service.

---

### 4.4 Versioned Keys

Include version number in cache key.

```
Cache Key: "menu:restaurant_123:v42"

When menu updates:
- Increment version to v43
- New key: "menu:restaurant_123:v43"
- Old key (v42) naturally expires or is ignored

Benefits:
- No explicit invalidation needed
- No race conditions
- Atomic switchover
```

**Pros:**
- No invalidation race conditions
- Atomic updates
- Old versions can serve during transition

**Cons:**
- Need to store/manage version numbers
- Old data sits in cache until TTL

**Best for:** High-traffic scenarios where atomic switchover matters.

---

## 5. Caching Technologies

### 5.1 Redis

**Type:** In-memory data store, often used as cache

**Characteristics:**
- Key-value store with rich data structures
- Sub-millisecond latency
- Persistence options (RDB, AOF)
- Clustering for scale
- Pub/Sub for invalidation

**Strengths:**
- Fast (in-memory)
- Feature-rich (TTL, pub/sub, Lua scripting)
- Mature ecosystem
- Can be used for sessions, queues, rate limiting

**Weaknesses:**
- Requires separate infrastructure
- Memory-bound (can get expensive)
- Network hop (vs. in-process)

**Use for:** Distributed caching, session storage, pub/sub.

---

### 5.2 Memcached

**Type:** Simple in-memory key-value cache

**Characteristics:**
- String keys, blob values
- Simple protocol
- Multi-threaded
- No persistence

**Strengths:**
- Simple and fast
- Mature and stable
- Lower memory overhead than Redis

**Weaknesses:**
- Fewer features than Redis
- No data structures
- No pub/sub

**Use for:** Simple, high-throughput caching.

---

### 5.3 Application-Level Cache (In-Process)

**Type:** Cache within application memory (e.g., Node.js Map, Guava Cache)

**Characteristics:**
- Lives in application process
- No network hop
- Lost on restart
- Not shared across instances

**Strengths:**
- Fastest (no network)
- Simple to implement
- No infrastructure

**Weaknesses:**
- Limited to process memory
- Not shared (each instance has own cache)
- Lost on restart
- Invalidation across instances is hard

**Use for:** Small, read-heavy data that doesn't need sharing.

---

### 5.4 CDN (Edge Caching)

**Type:** Distributed cache at edge locations

**Characteristics:**
- Geographically distributed
- Caches HTTP responses
- Controlled via headers (Cache-Control, ETag)

**Strengths:**
- Global, close to users
- Offloads origin server
- Great for static assets

**Weaknesses:**
- Only works for HTTP
- Limited invalidation control
- Not suitable for real-time data

**Use for:** Static assets, public API responses with long TTL.

---

### 5.5 Comparison

| Feature | Redis | Memcached | In-Process | CDN |
|---------|-------|-----------|------------|-----|
| Latency | ~1ms | ~1ms | ~0.001ms | Variable |
| Shared across instances | Yes | Yes | No | Yes |
| Data structures | Rich | Simple | Depends | None |
| TTL support | Yes | Yes | Manual | Yes |
| Pub/Sub | Yes | No | No | No |
| Persistence | Optional | No | No | Yes |
| Infrastructure | Required | Required | None | Required |
| Best for | General caching | Simple caching | Hot paths | Static content |

---

## 6. Real-Time Data Caching Approaches

### 6.1 The Real-Time Challenge

When data changes in real-time (like menu availability), traditional caching becomes tricky:

- **TTL-based:** User sees stale availability for up to TTL duration
- **No caching:** Every request hits database
- **Event-driven:** Requires infrastructure but solves the problem

### 6.2 Approach A: Short TTL

```
Strategy: Set very short TTL (5-30 seconds)

┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  TTL: 10 seconds                                                │
│                                                                 │
│  Pros:                           Cons:                          │
│  - Simple                        - Stale for up to 10s          │
│  - No event infra needed         - Still hitting DB often       │
│  - Reduces load by 90%+          - May show unavailable item    │
│                                                                 │
│  Acceptable if: Business can tolerate 10s staleness             │
│  Example: User tries to order, gets "item unavailable" at       │
│           checkout (annoying but not catastrophic)              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

### 6.3 Approach B: Event-Driven Invalidation

```
Strategy: Invalidate cache immediately when data changes

┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  1. Restaurant marks item unavailable                           │
│  2. "ItemAvailabilityChanged" event published                   │
│  3. Cache listener receives event                               │
│  4. Cache invalidates "menu:restaurant_123"                     │
│  5. Next request fetches fresh data from DB                     │
│                                                                 │
│  Pros:                           Cons:                          │
│  - No stale data                 - Requires message queue       │
│  - Immediate consistency         - More complex                 │
│  - Works with long TTL           - Event delivery not instant   │
│                                                                 │
│  Best when: Real-time accuracy is critical                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

### 6.4 Approach C: Hybrid (Stable + Volatile)

```
Strategy: Cache different data differently

┌─────────────────────────────────────────────────────────────────┐
│                    HYBRID CACHING                               │
└─────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│                  Restaurant Object                             │
│                                                                │
│  ┌───────────────────────┐   ┌───────────────────────┐        │
│  │   STABLE DATA         │   │   VOLATILE DATA        │        │
│  │                       │   │                        │        │
│  │  - Restaurant name    │   │  - isOpen              │        │
│  │  - Address            │   │  - Item availability   │        │
│  │  - Logo               │   │  - Current wait time   │        │
│  │  - Menu items         │   │                        │        │
│  │  - Prices             │   │                        │        │
│  │                       │   │                        │        │
│  │  TTL: 1 hour          │   │  TTL: 30 seconds       │        │
│  │  OR event-invalidated │   │  OR real-time fetch    │        │
│  └───────────────────────┘   └───────────────────────┘        │
│                                                                │
└───────────────────────────────────────────────────────────────┘

API Response combines:
- Cached stable data (fast)
- Fresh volatile data (accurate)
```

**Pros:**
- Best of both worlds
- Reduces DB load significantly
- Real-time where needed

**Cons:**
- More complex to implement
- Two data paths
- Need to define what's stable vs volatile

---

### 6.5 Approach D: Cache + Real-Time Updates (WebSocket)

```
Strategy: Cache for initial load, push updates via WebSocket

┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  1. Client loads menu (from cache - fast)                       │
│  2. Client connects to WebSocket                                │
│  3. Server pushes availability changes                          │
│  4. Client updates UI without refetching                        │
│                                                                 │
│  Pros:                           Cons:                          │
│  - Real-time UI updates          - WebSocket infrastructure     │
│  - Minimal DB load               - Connection management        │
│  - Great UX                      - More complex client          │
│                                                                 │
│  Best when: Rich client app (mobile, SPA)                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

### 6.6 Approach E: Optimistic Caching + Fallback

```
Strategy: Serve cached data, handle staleness at order time

┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  BROWSE FLOW:                                                   │
│  - Serve from cache (TTL: 1 minute)                             │
│  - Fast, may be slightly stale                                  │
│                                                                 │
│  ORDER FLOW:                                                    │
│  - Validate availability against DB (real-time check)          │
│  - If item unavailable: return friendly error                   │
│  - "Sorry, this item just sold out"                             │
│                                                                 │
│  Pros:                           Cons:                          │
│  - Simple to implement           - UX: add to cart, then fail   │
│  - Fast browsing                 - Depends on order validation  │
│  - No complex infra              - Only works if order validates│
│                                                                 │
│  Best when: Occasional staleness is acceptable                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 7. Trade-offs Analysis

### 7.1 Decision Matrix

| Approach | Complexity | Freshness | DB Load Reduction | Infrastructure |
|----------|------------|-----------|-------------------|----------------|
| **No Caching** | None | 100% fresh | 0% | None |
| **Short TTL (10s)** | Low | ~90% | ~95% | Redis |
| **Event-Driven** | Medium-High | ~99% | ~95% | Redis + Queue |
| **Hybrid** | Medium | Variable | ~90% | Redis |
| **WebSocket Push** | High | 100% | ~99% | Redis + WS |
| **Optimistic + Fallback** | Low | ~85% | ~90% | Redis |

### 7.2 For Q2 Scale (15-50 restaurants)

| Approach | Verdict | Reasoning |
|----------|---------|-----------|
| **Short TTL** | Recommended | Simple, effective, acceptable staleness |
| **Event-Driven** | Consider | We already have RabbitMQ for notifications |
| **Hybrid** | Consider | Good balance if data classification is clear |
| **WebSocket** | Overkill | Not needed for Q2 scale |
| **Optimistic** | Good fallback | Should implement regardless |

---

## 8. Application to Q2

### 8.1 Recommendation

**Primary Strategy:** Short TTL + Event-Driven Invalidation (Hybrid)

Since we're already adding RabbitMQ for notifications, we can reuse it for cache invalidation with minimal additional complexity.

```
┌─────────────────────────────────────────────────────────────────┐
│                    Q2 CACHING ARCHITECTURE                       │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐                                    ┌─────────────┐
│  Customer   │                                    │ Restaurant  │
│   Client    │                                    │  Dashboard  │
└──────┬──────┘                                    └──────┬──────┘
       │                                                  │
       │ 1. Browse restaurants                            │ 4. Update
       │                                                  │    availability
       ▼                                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                        API GATEWAY                               │
└─────────────────────────────────────────────────────────────────┘
       │                                                  │
       │                                                  │
       ▼                                                  ▼
┌─────────────┐                                    ┌─────────────┐
│    Redis    │◀──── 6. Invalidate ────────────────│    Menu     │
│    Cache    │                                    │   Service   │
│             │                                    │             │
│  TTL: 60s   │                                    │  5. Publish │
│  (fallback) │                                    │    event    │
└──────┬──────┘                                    └──────┬──────┘
       │                                                  │
       │ 2. Cache hit? Return                             │
       │    Cache miss? Fetch from DB                     ▼
       │                                           ┌─────────────┐
       │ 3. Return menu                            │  RabbitMQ   │
       │                                           │             │
       ▼                                           │ (reuse for  │
┌─────────────┐                                    │  notifs +   │
│ PostgreSQL  │                                    │  cache inv) │
│             │                                    └─────────────┘
└─────────────┘
```

### 8.2 Cache Configuration

| Data | TTL | Invalidation | Key Pattern |
|------|-----|--------------|-------------|
| Restaurant list | 5 min | On restaurant update | `restaurants:city:{city_id}` |
| Restaurant detail | 5 min | On restaurant update | `restaurant:{id}` |
| Menu items | 1 min | On item update | `menu:{restaurant_id}` |
| Item availability | 30 sec | Event-driven | `availability:{restaurant_id}` |

### 8.3 Fallback Strategy

Always implement optimistic fallback:
1. Serve from cache (fast)
2. Validate at order time (accurate)
3. Handle gracefully if item unavailable

---

## 9. References

### Books

| Book | Author | Notes |
|------|--------|-------|
| **Designing Data-Intensive Applications** | Martin Kleppmann | Chapter on caching and replication |
| **Database Internals** | Alex Petrov | Cache consistency patterns |
| **System Design Interview** | Alex Xu | Caching strategies chapter |

### Articles

| Article | Source | Link |
|---------|--------|------|
| **Caching Strategies and How to Choose the Right One** | AWS | https://aws.amazon.com/caching/best-practices/ |
| **Look Aside Caching vs Write Through** | Redis | https://redis.io/docs/manual/patterns/ |
| **Cache Invalidation Strategies** | Cloudflare | https://blog.cloudflare.com/cache-invalidation/ |
| **Everything You Need to Know About Caching** | DigitalOcean | https://www.digitalocean.com/community/tutorials/web-caching-basics |

### Videos

| Talk | Speaker | Link |
|------|---------|------|
| **Scaling Memcache at Facebook** | Facebook Engineering | https://www.usenix.org/conference/nsdi13/technical-sessions/presentation/nishtala |
| **Caching at Netflix** | Netflix Tech Blog | https://netflixtechblog.com/caching-for-a-global-netflix-7bcc457012f1 |

### Tools

| Tool | Purpose | Link |
|------|---------|------|
| **Redis** | Primary cache | https://redis.io/ |
| **Redis Insight** | Redis GUI | https://redis.com/redis-enterprise/redis-insight/ |
| **node-cache** | In-process Node.js cache | https://www.npmjs.com/package/node-cache |

---

## Document Summary

For Q2's restaurant browsing problem with real-time availability:

1. **Add Redis caching** for restaurant and menu data
2. **Use short TTL** (30-60 seconds) for availability data
3. **Leverage RabbitMQ** (already planned) for event-driven invalidation
4. **Implement optimistic fallback** - validate at order time
5. **Separate stable from volatile** data in cache design

This approach reduces database load by ~90%+ while maintaining acceptable data freshness for Q2 scale.
