# Reporting Database Separation Research

## Document Purpose

This document provides research on strategies to separate reporting workloads from operational (OLTP) workloads. The goal is to inform our decision for Question 2's reporting performance problem.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [OLTP vs OLAP Workloads](#2-oltp-vs-olap-workloads)
3. [Option 1: Read Replicas](#3-option-1-read-replicas)
4. [Option 2: Separate Reporting Database](#4-option-2-separate-reporting-database)
5. [Option 3: Materialized Views](#5-option-3-materialized-views)
6. [Option 4: Time-Based Restrictions](#6-option-4-time-based-restrictions)
7. [Option 5: CQRS Pattern](#7-option-5-cqrs-pattern)
8. [Option 6: Data Warehouse](#8-option-6-data-warehouse)
9. [Trade-offs Analysis](#9-trade-offs-analysis)
10. [Application to Q2](#10-application-to-q2)
11. [References](#11-references)

---

## 1. The Problem We're Solving

**Symptom:** Reporting queries are affecting normal application performance.

**Q2 Context:**
- 15 restaurants (expecting 50)
- More orders, customers, deliveries = more data
- Reports need aggregations (SUM, COUNT, GROUP BY)
- Complex queries can lock tables or consume resources
- Same database serves both operational and reporting needs

**Goal:** Allow reports to run without impacting order processing.

---

## 2. OLTP vs OLAP Workloads

### 2.1 Definitions

| Term | Full Name | Purpose |
|------|-----------|---------|
| **OLTP** | Online Transaction Processing | Day-to-day operations (orders, payments) |
| **OLAP** | Online Analytical Processing | Analysis and reporting |

### 2.2 Characteristics Comparison

| Characteristic | OLTP | OLAP |
|----------------|------|------|
| **Query type** | Simple, predefined | Complex, ad-hoc |
| **Operations** | INSERT, UPDATE, SELECT by ID | Aggregations, JOINs |
| **Data scope** | Single record or small set | Large datasets |
| **Response time** | Milliseconds | Seconds to minutes |
| **Concurrency** | High (many users) | Low (few analysts) |
| **Data freshness** | Real-time | Slightly stale OK |
| **Example** | "Get order #123" | "Total revenue by restaurant this month" |

### 2.3 Why They Conflict

```
┌─────────────────────────────────────────────────────────────────┐
│                    RESOURCE CONTENTION                          │
└─────────────────────────────────────────────────────────────────┘

       OLTP Queries                      OLAP Queries
       (many small)                      (few large)

    ┌────┐┌────┐┌────┐                   ┌─────────────────────┐
    │    ││    ││    │                   │                     │
    │ Q1 ││ Q2 ││ Q3 │ ...               │   REPORT QUERY      │
    │    ││    ││    │                   │   (full table scan) │
    └────┘└────┘└────┘                   └─────────────────────┘
        │    │    │                               │
        │    │    │                               │
        ▼    ▼    ▼                               ▼
    ┌─────────────────────────────────────────────────────────────┐
    │                       DATABASE                               │
    │                                                              │
    │  ┌─────────────────────────────────────────────────────┐    │
    │  │  CPU / Memory / Disk I/O / Connection Pool         │    │
    │  │                                                     │    │
    │  │  CONTENTION: Report query consumes resources        │    │
    │  │  → OLTP queries slow down or timeout                │    │
    │  │  → Locks may block writes                           │    │
    │  └─────────────────────────────────────────────────────┘    │
    │                                                              │
    └─────────────────────────────────────────────────────────────┘
```

**Common symptoms:**
- Slow order placement during report generation
- Database CPU spikes during business hours
- Table locks causing write timeouts
- Connection pool exhaustion

---

## 3. Option 1: Read Replicas

### 3.1 Concept

Create a copy of the primary database that receives real-time updates via streaming replication. Reports run against the replica, not the primary.

### 3.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    READ REPLICA ARCHITECTURE                     │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐                              ┌─────────────┐
│  App Server │                              │  Reporting  │
│             │                              │   Service   │
└──────┬──────┘                              └──────┬──────┘
       │                                            │
       │ Writes + Reads                             │ Reads only
       │                                            │
       ▼                                            ▼
┌─────────────┐                              ┌─────────────┐
│   PRIMARY   │───── Streaming ─────────────▶│   REPLICA   │
│  PostgreSQL │      Replication             │  PostgreSQL │
│             │      (WAL shipping)          │  (read-only)│
└─────────────┘                              └─────────────┘
       │                                            │
       │                                            │
       ▼                                            ▼
  Handles OLTP                              Handles OLAP
  (orders, payments)                        (reports, analytics)
```

### 3.3 PostgreSQL Streaming Replication

PostgreSQL supports built-in streaming replication:

1. **Primary** writes to WAL (Write-Ahead Log)
2. **Replica** streams WAL and applies changes
3. Replication lag typically milliseconds to seconds
4. Replica is read-only (no writes)

**Configuration highlights:**
```
# postgresql.conf (primary)
wal_level = replica
max_wal_senders = 3

# recovery.conf (replica)
standby_mode = on
primary_conninfo = 'host=primary_ip port=5432'
```

### 3.4 Pros and Cons

| Pros | Cons |
|------|------|
| Simple to set up with PostgreSQL | Replication lag (usually minor) |
| No application changes for reads | Same schema as primary (not optimized for OLAP) |
| Real-time or near real-time data | Still complex queries (not pre-aggregated) |
| Can also serve as failover | Additional infrastructure cost |
| Battle-tested, widely used | Read-only (can't write to replica) |

### 3.5 When to Use

- Reports need near real-time data (seconds stale OK)
- Don't want to change schema or ETL data
- Simple setup preferred
- Also want high availability (failover)

### 3.6 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | +1 PostgreSQL instance |
| Setup complexity | Low (native PostgreSQL) |
| Application changes | Minimal (route reports to replica) |
| Maintenance | Low (automatic sync) |
| Cloud support | Excellent (AWS RDS, GCP, Azure all support) |

---

## 4. Option 2: Separate Reporting Database

### 4.1 Concept

Create a dedicated database optimized for reporting. Data is synced from the operational database via ETL (Extract, Transform, Load) or CDC (Change Data Capture).

### 4.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                 SEPARATE REPORTING DATABASE                      │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐                              ┌─────────────┐
│  App Server │                              │  Reporting  │
│             │                              │   Service   │
└──────┬──────┘                              └──────┬──────┘
       │                                            │
       │ OLTP                                       │ OLAP
       │                                            │
       ▼                                            ▼
┌─────────────┐                              ┌─────────────┐
│ Operational │       ┌───────────┐          │  Reporting  │
│  Database   │──────▶│  ETL/CDC  │─────────▶│  Database   │
│ (PostgreSQL)│       │  Process  │          │ (optimized) │
└─────────────┘       └───────────┘          └─────────────┘
                                                    │
       Normalized schema                            │
       Optimized for writes                         │
                                              Denormalized schema
                                              Pre-aggregated tables
                                              Optimized for reads
```

### 4.3 Sync Methods

**ETL (Extract, Transform, Load):**
- Batch process (hourly, daily)
- Extract data from operational DB
- Transform (denormalize, aggregate)
- Load into reporting DB

**CDC (Change Data Capture):**
- Real-time or near real-time
- Capture changes as they happen
- Stream to reporting database
- Tools: Debezium, AWS DMS, Postgres logical replication

### 4.4 Reporting Database Options

| Database | Type | Best For |
|----------|------|----------|
| **PostgreSQL** | Same as operational | Simple setup, familiar |
| **ClickHouse** | Columnar | High-performance analytics |
| **TimescaleDB** | Time-series | Time-based analytics |
| **Elasticsearch** | Search/Analytics | Full-text + aggregations |
| **BigQuery/Redshift** | Cloud warehouse | Large-scale analytics |

### 4.5 Pros and Cons

| Pros | Cons |
|------|------|
| Schema optimized for reporting | More infrastructure |
| Pre-aggregated data (fast queries) | ETL development and maintenance |
| Complete isolation | Data freshness depends on sync frequency |
| Can use specialized OLAP database | Schema divergence risk |
| Historical data retention | More complex architecture |

### 4.6 When to Use

- Reports are complex and slow even on replica
- Need pre-aggregated data for dashboard performance
- Want to retain historical data longer than operational
- Have resources for ETL development

### 4.7 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | +1 database + ETL process |
| Setup complexity | Medium-High |
| Application changes | Medium (new schemas, new queries) |
| Maintenance | Higher (ETL jobs, schema sync) |
| Skill required | Data engineering |

---

## 5. Option 3: Materialized Views

### 5.1 Concept

Create pre-computed views that store query results. Refresh periodically. Reports query the materialized view instead of running complex queries.

### 5.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    MATERIALIZED VIEWS                            │
└─────────────────────────────────────────────────────────────────┘

STANDARD VIEW (computed on every query):

  SELECT * FROM orders_summary_view;

       │
       ▼
  ┌─────────────────────────────────────────────┐
  │ SELECT restaurant_id, SUM(total), COUNT(*)  │
  │ FROM orders                                  │
  │ GROUP BY restaurant_id                       │
  └─────────────────────────────────────────────┘
       │
       ▼
  [Full table scan every time - SLOW]


MATERIALIZED VIEW (pre-computed, stored):

  CREATE MATERIALIZED VIEW orders_summary AS
  SELECT restaurant_id, SUM(total), COUNT(*)
  FROM orders
  GROUP BY restaurant_id;

       │
       ▼
  ┌─────────────────────────────────────────────┐
  │  Stored on disk as a table                  │
  │  Fast to query (already computed)           │
  │  Refresh periodically:                      │
  │  REFRESH MATERIALIZED VIEW orders_summary;  │
  └─────────────────────────────────────────────┘
       │
       ▼
  [Fast query against pre-computed data]
```

### 5.3 PostgreSQL Materialized Views

```sql
-- Create materialized view
CREATE MATERIALIZED VIEW daily_revenue AS
SELECT
    restaurant_id,
    DATE(created_at) as order_date,
    SUM(total_amount) as revenue,
    COUNT(*) as order_count
FROM orders
WHERE state = 'COMPLETED'
GROUP BY restaurant_id, DATE(created_at);

-- Refresh (full)
REFRESH MATERIALIZED VIEW daily_revenue;

-- Refresh concurrently (no lock, requires unique index)
REFRESH MATERIALIZED VIEW CONCURRENTLY daily_revenue;
```

### 5.4 Pros and Cons

| Pros | Cons |
|------|------|
| Simple (native PostgreSQL) | Data staleness (depends on refresh) |
| No new infrastructure | Refresh can be expensive |
| Fast report queries | Still same database (resource contention during refresh) |
| Easy to create and modify | Not suitable for real-time |
| Can index materialized views | Storage overhead |

### 5.5 When to Use

- Reports don't need real-time data
- Aggregations are complex but predictable
- Don't want additional infrastructure
- OK with periodic refresh (hourly, daily)

### 5.6 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | None (same database) |
| Setup complexity | Low |
| Application changes | Minimal (query view instead of tables) |
| Maintenance | Low (schedule refresh) |
| Refresh contention | Risk (can lock during refresh) |

---

## 6. Option 4: Time-Based Restrictions

### 6.1 Concept

Restrict when heavy reports can run. Schedule reports during off-peak hours.

### 6.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    TIME-BASED RESTRICTIONS                       │
└─────────────────────────────────────────────────────────────────┘

      PEAK HOURS (11am - 2pm, 6pm - 9pm)     OFF-PEAK (2am - 6am)
      ─────────────────────────────────      ───────────────────────

      ┌─────────────────────────┐            ┌─────────────────────┐
      │                         │            │                     │
      │  OLTP queries only      │            │  OLTP + Reports OK  │
      │  Reports blocked/queued │            │  Heavy jobs run     │
      │                         │            │                     │
      └─────────────────────────┘            └─────────────────────┘


Implementation options:

1. Application-level: UI disables report buttons during peak
2. Scheduling: Cron jobs run reports overnight, cache results
3. Database-level: Resource limits per user/role
```

### 6.3 Implementation Approaches

**Scheduled Report Generation:**
```
┌───────────┐     2am: Run reports     ┌───────────┐
│   Cron    │─────────────────────────▶│  Database │
│   Job     │                          │           │
└─────┬─────┘                          └───────────┘
      │
      │ Store results
      ▼
┌───────────┐
│   Cache   │◀──── Daytime: Users read cached reports
│  / File   │
└───────────┘
```

**Resource Governor (database-level):**
```sql
-- PostgreSQL: Use roles with statement_timeout
CREATE ROLE reporter WITH LOGIN PASSWORD 'xxx';
ALTER ROLE reporter SET statement_timeout = '30s';

-- Limit connections
ALTER ROLE reporter CONNECTION LIMIT 2;
```

### 6.4 Pros and Cons

| Pros | Cons |
|------|------|
| No infrastructure changes | Reports not available on-demand |
| Simple to implement | Stale data (reports from last night) |
| Zero cost | Doesn't scale (more reports = longer window) |
| Immediate relief | User experience impact |
| Good for batch reports | Not for real-time dashboards |

### 6.5 When to Use

- Quick fix while planning better solution
- Reports truly don't need real-time data
- Limited resources (can't add infrastructure)
- Batch/email reports (not interactive dashboards)

### 6.6 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | None |
| Setup complexity | Very Low |
| Application changes | Minimal |
| Maintenance | Very Low |
| User impact | High (limited access) |

---

## 7. Option 5: CQRS Pattern

### 7.1 Concept

**Command Query Responsibility Segregation** - separate the models for reading and writing. Commands (writes) use one model, Queries (reads) use a different, optimized model.

### 7.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                        CQRS PATTERN                              │
└─────────────────────────────────────────────────────────────────┘

                    ┌─────────────┐
                    │   Client    │
                    └──────┬──────┘
                           │
             ┌─────────────┴─────────────┐
             │                           │
        COMMANDS                     QUERIES
        (writes)                     (reads)
             │                           │
             ▼                           ▼
      ┌─────────────┐            ┌─────────────┐
      │  Command    │            │   Query     │
      │  Handler    │            │   Handler   │
      └──────┬──────┘            └──────┬──────┘
             │                           │
             ▼                           ▼
      ┌─────────────┐            ┌─────────────┐
      │   Write     │   Sync     │    Read     │
      │   Model     │───────────▶│    Model    │
      │ (normalized)│  (events)  │(denormalized│
      └─────────────┘            └─────────────┘
             │                           │
             ▼                           ▼
      ┌─────────────┐            ┌─────────────┐
      │  Write DB   │            │   Read DB   │
      └─────────────┘            └─────────────┘
```

### 7.3 Sync Mechanisms

**Event-Driven Sync:**
1. Command creates/modifies entity
2. Domain event published (e.g., "OrderPlaced")
3. Read model handler receives event
4. Updates denormalized read database

**Direct Sync:**
1. Command writes to write database
2. Same transaction/process updates read model
3. Simpler but less decoupled

### 7.4 Read Model Design

The read model is designed for specific queries:

```
WRITE MODEL (normalized):                READ MODEL (denormalized):

orders                                   restaurant_dashboard
├── order_id                            ├── restaurant_id
├── customer_id                         ├── today_orders_count
├── restaurant_id                       ├── today_revenue
├── total_amount                        ├── pending_orders_count
├── state                               ├── avg_preparation_time
└── created_at                          └── updated_at

order_items                              No joins needed!
├── order_item_id                        One table, one query
├── order_id                             Pre-computed aggregates
├── item_id
└── quantity
```

### 7.5 Pros and Cons

| Pros | Cons |
|------|------|
| Optimized read performance | Significant complexity |
| Each model serves its purpose | Eventual consistency |
| Scales reads independently | Two models to maintain |
| Decoupled (async updates) | Event infrastructure needed |
| Good for event sourcing | Debugging harder |

### 7.6 When to Use

- Significant difference between read/write patterns
- High read-to-write ratio
- Need to scale reads independently
- Already using events/messaging
- Complex domain with many read views

### 7.7 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | Read DB + Event Bus |
| Setup complexity | High |
| Application changes | Significant (separate models) |
| Maintenance | High (two models, sync logic) |
| Skill required | Event-driven design expertise |

---

## 8. Option 6: Data Warehouse

### 8.1 Concept

Use a dedicated analytical database (data warehouse) designed for OLAP workloads. Modern options include cloud-native warehouses.

### 8.2 Options

| Warehouse | Type | Best For |
|-----------|------|----------|
| **Amazon Redshift** | Columnar | AWS ecosystem, large scale |
| **Google BigQuery** | Serverless | Pay-per-query, easy setup |
| **Snowflake** | Multi-cloud | Flexibility, scalability |
| **Azure Synapse** | Integrated | Azure ecosystem |
| **ClickHouse** | Self-hosted | Performance, open source |
| **Apache Druid** | Real-time | Low latency analytics |

### 8.3 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    DATA WAREHOUSE PATTERN                        │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐     CDC/ETL      ┌─────────────┐
│ Operational │─────────────────▶│    Data     │
│  Database   │                  │    Lake     │
└─────────────┘                  │  (S3, GCS)  │
                                 └──────┬──────┘
                                        │
                                        │ Transform
                                        │ (dbt, Spark)
                                        ▼
                                 ┌─────────────┐
                                 │    Data     │
                                 │  Warehouse  │
                                 │ (Redshift,  │
                                 │  BigQuery)  │
                                 └──────┬──────┘
                                        │
                    ┌───────────────────┼───────────────────┐
                    │                   │                   │
                    ▼                   ▼                   ▼
             ┌───────────┐       ┌───────────┐       ┌───────────┐
             │   BI Tool │       │ Dashboards│       │   Ad-hoc  │
             │ (Tableau) │       │ (internal)│       │  Queries  │
             └───────────┘       └───────────┘       └───────────┘
```

### 8.4 Pros and Cons

| Pros | Cons |
|------|------|
| Built for analytics at scale | Significant cost |
| Handles petabytes | Complex setup |
| SQL interface (familiar) | Data latency (ETL lag) |
| BI tool integrations | Overkill for small scale |
| Managed (cloud options) | New technology to learn |

### 8.5 When to Use

- Large data volumes (millions of rows)
- Complex analytical queries
- Multiple data sources to combine
- BI/reporting team with dedicated needs
- Budget available for infrastructure

### 8.6 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | High (warehouse + ETL pipeline) |
| Setup complexity | High |
| Cost | Significant (cloud warehouses) |
| Maintenance | Medium (managed services) |
| Skill required | Data engineering |

---

## 9. Trade-offs Analysis

### 9.1 Decision Matrix

| Option | Complexity | Cost | Freshness | Performance Gain | Setup Time |
|--------|------------|------|-----------|------------------|------------|
| **Read Replica** | Low | Low | Near real-time | Good | Hours |
| **Separate DB** | Medium | Medium | Minutes-Hours | Excellent | Days |
| **Materialized Views** | Low | None | Depends on refresh | Good | Hours |
| **Time Restrictions** | Very Low | None | Stale | N/A (avoidance) | Hours |
| **CQRS** | High | Medium | Near real-time | Excellent | Weeks |
| **Data Warehouse** | High | High | Hours | Excellent | Weeks |

### 9.2 For Q2 Scale (15-50 restaurants)

| Option | Verdict | Reasoning |
|--------|---------|-----------|
| **Read Replica** | **Recommended** | Simple, effective, fits scale |
| **Materialized Views** | Good addition | Use with replica for extra speed |
| **Time Restrictions** | Quick fix | Immediate relief while setting up replica |
| **Separate DB** | Overkill | Not enough data complexity yet |
| **CQRS** | Overkill | Too much architecture for the problem |
| **Data Warehouse** | Overkill | Designed for much larger scale |

### 9.3 Progression Path

```
Q2 (15-50 restaurants):
  Read Replica + Materialized Views

Q3 (if needed):
  Separate Reporting DB or CQRS elements

Future (100+ restaurants):
  Consider Data Warehouse
```

---

## 10. Application to Q2

### 10.1 Recommendation

**Primary Strategy:** PostgreSQL Read Replica

```
┌─────────────────────────────────────────────────────────────────┐
│                    Q2 REPORTING ARCHITECTURE                     │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐                              ┌─────────────────────┐
│   Order     │                              │     Reporting       │
│   Service   │                              │      Service        │
│  (writes)   │                              │     (reads)         │
└──────┬──────┘                              └──────────┬──────────┘
       │                                                │
       │                                                │
       ▼                                                ▼
┌─────────────┐     Streaming            ┌─────────────────────────┐
│   PRIMARY   │     Replication          │        REPLICA          │
│  PostgreSQL │─────────────────────────▶│      PostgreSQL         │
│             │     (< 1 second lag)     │     (read-only)         │
│  - OLTP     │                          │                         │
│  - Writes   │                          │  - Reports              │
│             │                          │  - Dashboards           │
└─────────────┘                          │  - Analytics            │
                                         │                         │
                                         │  + Materialized Views   │
                                         │    for common reports   │
                                         └─────────────────────────┘
```

### 10.2 Implementation Steps

1. **Set up PostgreSQL streaming replication**
   - Configure primary for replication
   - Create replica instance
   - Verify replication lag is acceptable

2. **Route reporting queries to replica**
   - Application uses replica connection for reports
   - Use connection pooler (PgBouncer) for efficiency

3. **Add materialized views on replica for common reports**
   - Daily revenue by restaurant
   - Order counts by status
   - Refresh during off-peak (e.g., hourly)

4. **Monitor replication lag**
   - Alert if lag exceeds threshold
   - Most reports tolerate seconds of lag

### 10.3 Materialized Views to Create

| View Name | Purpose | Refresh |
|-----------|---------|---------|
| `daily_revenue_summary` | Revenue by restaurant by day | Hourly |
| `order_status_counts` | Orders by status | Every 15 min |
| `delivery_performance` | Avg delivery times | Hourly |
| `customer_order_history` | Orders per customer | Daily |

### 10.4 Connection Configuration

```javascript
// Application config
const primaryDB = new Pool({
  host: 'primary.db.example.com',
  // ... used for writes and OLTP reads
});

const replicaDB = new Pool({
  host: 'replica.db.example.com',
  // ... used for reports and analytics
});

// Usage
async function getOrderById(orderId) {
  return primaryDB.query('SELECT * FROM orders WHERE id = $1', [orderId]);
}

async function generateRevenueReport(restaurantId) {
  return replicaDB.query('SELECT * FROM daily_revenue_summary WHERE restaurant_id = $1', [restaurantId]);
}
```

---

## 11. References

### Documentation

| Resource | Link |
|----------|------|
| PostgreSQL Streaming Replication | https://www.postgresql.org/docs/current/warm-standby.html |
| PostgreSQL Materialized Views | https://www.postgresql.org/docs/current/rules-materializedviews.html |
| AWS RDS Read Replicas | https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_ReadRepl.html |

### Articles

| Article | Source |
|---------|--------|
| CQRS Pattern | https://martinfowler.com/bliki/CQRS.html |
| Event Sourcing | https://martinfowler.com/eaaDev/EventSourcing.html |
| Materialized Views Best Practices | https://www.citusdata.com/blog/2018/10/31/materialized-views-in-postgres/ |

### Books

| Book | Author | Relevance |
|------|--------|-----------|
| Designing Data-Intensive Applications | Martin Kleppmann | Replication, OLTP vs OLAP |
| Building Event-Driven Microservices | Adam Bellemare | CQRS, Event Sourcing |

---

## Document Summary

For Q2's reporting performance problem:

1. **Implement PostgreSQL Read Replica** for reporting queries
2. **Add materialized views** on replica for common aggregations
3. **Route reporting traffic** to replica in application
4. **Monitor replication lag** to ensure data freshness

This provides immediate relief with minimal complexity, and scales well for Q2's 15-50 restaurant range. More sophisticated solutions (CQRS, Data Warehouse) can be considered if/when the scale demands it.
