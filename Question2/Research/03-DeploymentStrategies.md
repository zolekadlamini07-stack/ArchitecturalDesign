# Deployment Strategies Research

## Document Purpose

This document provides research on deployment strategies for applications, focusing on reducing deployment disruption. The goal is to inform our decision for Question 2's disruptive deployment problem.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Deployment Fundamentals](#2-deployment-fundamentals)
3. [Option 1: Feature Flags](#3-option-1-feature-flags)
4. [Option 2: Blue-Green Deployments](#4-option-2-blue-green-deployments)
5. [Option 3: Rolling Deployments](#5-option-3-rolling-deployments)
6. [Option 4: Canary Releases](#6-option-4-canary-releases)
7. [Option 5: A/B Testing Deployments](#7-option-5-ab-testing-deployments)
8. [Database Migration Strategies](#8-database-migration-strategies)
9. [Trade-offs Analysis](#9-trade-offs-analysis)
10. [Application to Q2](#10-application-to-q2)
11. [References](#11-references)

---

## 1. The Problem We're Solving

**Symptom:** Deployments are becoming more disruptive.

**Q2 Context:**
- Modular monolith deployed as single unit
- More features = larger artifact, longer startup
- All-or-nothing deployment: any change requires full redeploy
- Deployment window affects all users
- Rollback means full rollback of everything

**Goal:** Reduce deployment risk and disruption while maintaining single deployment unit.

---

## 2. Deployment Fundamentals

### 2.1 What Makes Deployments Disruptive?

| Factor | Impact |
|--------|--------|
| **Downtime** | Users can't access the system |
| **Risk** | New code might have bugs |
| **Scope** | More changes = more risk |
| **Rollback** | How quickly can we undo? |
| **Coordination** | Multiple people/teams involved |

### 2.2 Deployment vs Release

```
┌─────────────────────────────────────────────────────────────────┐
│              DEPLOYMENT vs RELEASE                               │
└─────────────────────────────────────────────────────────────────┘

TRADITIONAL:
  Deployment = Release (same thing)

  ┌───────────┐     Deploy      ┌───────────┐
  │ Old Code  │────────────────▶│ New Code  │
  └───────────┘     = Users     └───────────┘
                    see change


MODERN (with feature flags):
  Deployment ≠ Release (decoupled)

  ┌───────────┐     Deploy      ┌───────────────────────────────┐
  │ Old Code  │────────────────▶│ New Code (feature disabled)   │
  └───────────┘                 └───────────────────────────────┘
                                            │
                                    Release │ (enable flag)
                                            ▼
                                ┌───────────────────────────────┐
                                │ New Code (feature enabled)    │
                                └───────────────────────────────┘

Benefits:
- Deploy anytime (low risk if feature off)
- Release when ready (business decision)
- Rollback = disable flag (instant)
```

### 2.3 Zero-Downtime Deployment Goals

1. **No service interruption** - Users always have access
2. **Graceful transitions** - Connections drained, not dropped
3. **Safe rollback** - Can revert quickly if issues found
4. **Independent timing** - Deploy and release separately

---

## 3. Option 1: Feature Flags

### 3.1 Concept

Wrap new features in conditional logic. Deploy code with feature disabled, then enable via configuration without redeployment.

### 3.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                      FEATURE FLAGS                               │
└─────────────────────────────────────────────────────────────────┘

CODE:
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  if (featureFlags.isEnabled('new-checkout-flow')) {             │
│      return newCheckoutFlow(order);                             │
│  } else {                                                       │
│      return oldCheckoutFlow(order);                             │
│  }                                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

FLAG CONFIGURATION:
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  {                                                              │
│    "new-checkout-flow": {                                       │
│      "enabled": false,              ← Deploy with flag off      │
│      "rollout_percentage": 0        ← Can gradually enable      │
│    }                                                            │
│  }                                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

WORKFLOW:
┌─────────┐     ┌─────────┐     ┌─────────┐     ┌─────────┐
│  Code   │────▶│ Deploy  │────▶│  Test   │────▶│ Enable  │
│  Ready  │     │ (off)   │     │ (staging│     │ (flag)  │
└─────────┘     └─────────┘     │  or 1%) │     └─────────┘
                                └─────────┘
                                    │
                               Issue found?
                                    │
                                    ▼
                               Disable flag
                               (instant rollback)
```

### 3.3 Types of Feature Flags

| Type | Purpose | Lifespan |
|------|---------|----------|
| **Release Flags** | Enable/disable features | Short (remove after stable) |
| **Ops Flags** | Control operational behavior | Long (circuit breakers) |
| **Experiment Flags** | A/B testing | Medium (duration of experiment) |
| **Permission Flags** | Feature access by user/tier | Long (business logic) |

### 3.4 Implementation Options

**Simple (Config File/Environment):**
```javascript
// config.json
{
  "features": {
    "newCheckout": process.env.FEATURE_NEW_CHECKOUT === 'true'
  }
}
```

**Database-Backed:**
```javascript
// Flags stored in database, changeable at runtime
async function isEnabled(flagName) {
  const flag = await db.query('SELECT enabled FROM feature_flags WHERE name = $1', [flagName]);
  return flag.enabled;
}
```

**Third-Party Services:**
- LaunchDarkly
- Split.io
- Flagsmith (open source)
- Unleash (open source)

### 3.5 Gradual Rollout

```
┌─────────────────────────────────────────────────────────────────┐
│                     GRADUAL ROLLOUT                              │
└─────────────────────────────────────────────────────────────────┘

Day 1: Deploy with flag off
       All users → Old flow

Day 2: Enable for 5% of users
       5% users → New flow (monitor for errors)
       95% users → Old flow

Day 3: Increase to 25%
       25% users → New flow (still looking good)
       75% users → Old flow

Day 4: Increase to 100%
       All users → New flow

Day 7: Remove flag and old code (cleanup)
```

**Percentage-Based Example:**
```javascript
function isEnabled(flagName, userId) {
  const flag = getFlag(flagName);

  if (!flag.enabled) return false;

  // Hash user ID to get consistent percentage bucket
  const bucket = hash(userId) % 100;
  return bucket < flag.rolloutPercentage;
}
```

### 3.6 Pros and Cons

| Pros | Cons |
|------|------|
| Deploy anytime (low risk) | Code complexity (if/else branches) |
| Instant rollback (disable flag) | Tech debt (must clean up flags) |
| Gradual rollout | Testing both paths needed |
| Per-user targeting | Configuration management overhead |
| Decouple deploy from release | Can accumulate "flag debt" |

### 3.7 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | Low (config or simple service) |
| Setup complexity | Low-Medium |
| Application changes | Medium (wrap features in flags) |
| Maintenance | Ongoing (clean up old flags) |
| Skill required | Low |

---

## 4. Option 2: Blue-Green Deployments

### 4.1 Concept

Maintain two identical production environments: "Blue" (current) and "Green" (new). Deploy to Green, test, then switch traffic instantly.

### 4.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    BLUE-GREEN DEPLOYMENT                         │
└─────────────────────────────────────────────────────────────────┘

INITIAL STATE (Blue is live):

    Users → Load Balancer → [BLUE: v1.0] ← LIVE
                            [GREEN: idle]


STEP 1: Deploy to Green:

    Users → Load Balancer → [BLUE: v1.0] ← LIVE
                            [GREEN: v1.1] ← Deployed, testing


STEP 2: Switch traffic (instant):

    Users → Load Balancer → [BLUE: v1.0] ← Standby (rollback ready)
                         ↘  [GREEN: v1.1] ← LIVE


ROLLBACK (if issues):

    Users → Load Balancer → [BLUE: v1.0] ← LIVE (switch back)
                            [GREEN: v1.1] ← Failed
```

### 4.3 Traffic Switching Methods

| Method | How | Speed |
|--------|-----|-------|
| **Load Balancer** | Update backend pool | Seconds |
| **DNS** | Update DNS records | Minutes (TTL) |
| **Router/Proxy** | Nginx config reload | Seconds |

### 4.4 Pros and Cons

| Pros | Cons |
|------|------|
| Zero downtime | 2x infrastructure cost |
| Instant rollback | Database schema challenges |
| Full environment testing | State synchronization |
| Simple mental model | Need traffic switch mechanism |
| Battle-tested pattern | Both environments must be in sync |

### 4.5 The Database Problem

```
┌─────────────────────────────────────────────────────────────────┐
│                 BLUE-GREEN + DATABASE                            │
└─────────────────────────────────────────────────────────────────┘

CHALLENGE:
  Both Blue and Green need to work with the same database.
  If Green requires schema changes, Blue might break.

SOLUTIONS:

1. Shared Database (backward-compatible migrations only):

   [BLUE: v1.0] ───┐
                   ├──→ [Database: supports both versions]
   [GREEN: v1.1]───┘

   - Add columns, don't remove
   - Add tables, don't drop
   - New code handles old data


2. Expand-Contract Migration:

   Phase 1 (Expand):
   - Add new column (nullable)
   - Deploy new code that writes to both columns
   - Migrate existing data

   Phase 2 (Contract - later):
   - Remove old column
   - Only after old code fully retired
```

### 4.6 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | High (2x environments) |
| Setup complexity | Medium |
| Application changes | Low (deployment process change) |
| Maintenance | Medium |
| Skill required | Medium (ops/devops) |

---

## 5. Option 3: Rolling Deployments

### 5.1 Concept

Gradually replace instances of the old version with the new version, one (or a few) at a time.

### 5.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    ROLLING DEPLOYMENT                            │
└─────────────────────────────────────────────────────────────────┘

INITIAL (4 instances, all v1.0):

    Load Balancer → [v1.0] [v1.0] [v1.0] [v1.0]


STEP 1: Update instance 1:

    Load Balancer → [v1.1] [v1.0] [v1.0] [v1.0]
                      ↑
                   (drained, updated, rejoined)


STEP 2: Update instance 2:

    Load Balancer → [v1.1] [v1.1] [v1.0] [v1.0]


STEP 3: Update instance 3:

    Load Balancer → [v1.1] [v1.1] [v1.1] [v1.0]


STEP 4: Update instance 4:

    Load Balancer → [v1.1] [v1.1] [v1.1] [v1.1]
                                            ↑
                                        COMPLETE
```

### 5.3 Key Considerations

**Graceful Shutdown:**
```
1. Remove instance from load balancer
2. Finish in-flight requests (drain)
3. Deploy new version
4. Health check passes
5. Add back to load balancer
```

**Mixed Version Handling:**
- During rollout, both v1.0 and v1.1 serve traffic
- Requests must work with either version
- Session affinity may help (same user hits same version)

### 5.4 Pros and Cons

| Pros | Cons |
|------|------|
| No extra infrastructure | Mixed versions during rollout |
| Gradual (catch issues early) | Slower than blue-green |
| Works with existing setup | Rollback is another rolling deploy |
| Kubernetes native support | Requires multiple instances |
| Minimal resource overhead | Complex if versions incompatible |

### 5.5 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | Low (existing instances) |
| Setup complexity | Medium |
| Application changes | Low |
| Maintenance | Low |
| Skill required | Medium (container orchestration) |

---

## 6. Option 4: Canary Releases

### 6.1 Concept

Deploy new version to a small subset of users/traffic. Monitor for issues. If successful, gradually expand to all users.

### 6.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                     CANARY RELEASE                               │
└─────────────────────────────────────────────────────────────────┘

INITIAL:
                        ┌──────────────┐
    100% traffic ──────▶│    v1.0      │
                        │  (stable)    │
                        └──────────────┘


CANARY DEPLOYED (5% traffic):

                        ┌──────────────┐
    95% traffic ───────▶│    v1.0      │
                        │  (stable)    │
                        └──────────────┘

                        ┌──────────────┐
    5% traffic ────────▶│    v1.1      │ ← CANARY
                        │  (monitor!)  │
                        └──────────────┘


IF CANARY HEALTHY (expand to 50%):

                        ┌──────────────┐
    50% traffic ───────▶│    v1.0      │
                        └──────────────┘

                        ┌──────────────┐
    50% traffic ───────▶│    v1.1      │
                        └──────────────┘


IF CANARY FAILS (instant rollback):

                        ┌──────────────┐
    100% traffic ──────▶│    v1.0      │ ← All traffic back
                        └──────────────┘

                        ┌──────────────┐
    0% traffic ────────▶│    v1.1      │ ← Killed
                        └──────────────┘
```

### 6.3 Canary Metrics to Monitor

| Metric | What to Watch |
|--------|---------------|
| **Error rate** | Should not increase |
| **Latency** | P50, P95, P99 should not degrade |
| **CPU/Memory** | Should be similar to stable |
| **Business metrics** | Conversion rates, order success |
| **Logs** | New errors or warnings |

### 6.4 Pros and Cons

| Pros | Cons |
|------|------|
| Limited blast radius | Complex routing needed |
| Real production testing | Monitoring infrastructure needed |
| Data-driven decisions | Mixed versions in production |
| Quick rollback | Requires traffic splitting |
| Confidence before full rollout | Slower full deployment |

### 6.5 Cost/Complexity

| Aspect | Assessment |
|--------|------------|
| Infrastructure | Medium (traffic routing) |
| Setup complexity | Medium-High |
| Application changes | Low |
| Maintenance | Medium |
| Skill required | Medium-High (monitoring, automation) |

---

## 7. Option 5: A/B Testing Deployments

### 7.1 Concept

Similar to canary, but specifically for testing feature variations with different user groups. Measure business impact, not just technical health.

### 7.2 How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    A/B TESTING                                   │
└─────────────────────────────────────────────────────────────────┘

EXPERIMENT: New Checkout Button Color

    User Group A (50%):
    ┌─────────────────────┐
    │ [Checkout: BLUE]    │ ← Control
    └─────────────────────┘

    User Group B (50%):
    ┌─────────────────────┐
    │ [Checkout: GREEN]   │ ← Variant
    └─────────────────────┘


ANALYSIS:

    Group A: 3.2% conversion rate
    Group B: 3.8% conversion rate
                    ↓
    Statistical significance?
    Yes → Roll out green button
    No  → Need more data or revert
```

### 7.3 Difference from Canary

| Aspect | Canary | A/B Testing |
|--------|--------|-------------|
| **Purpose** | Safe deployment | Measure impact |
| **Metrics** | Errors, latency | Business outcomes |
| **Duration** | Until stable (hours/days) | Until statistical significance (weeks) |
| **Rollback trigger** | Technical issues | No clear winner |
| **Goal** | Deploy new version | Find best version |

### 7.4 When to Use

- Measuring business impact of changes
- Comparing multiple approaches
- Data-driven product decisions
- Not for technical deployment safety

---

## 8. Database Migration Strategies

### 8.1 The Challenge

Database schema changes can break zero-downtime deployments:
- Old code expects old schema
- New code expects new schema
- During transition, both may be running

### 8.2 Expand-Contract Pattern

```
┌─────────────────────────────────────────────────────────────────┐
│                 EXPAND-CONTRACT MIGRATION                        │
└─────────────────────────────────────────────────────────────────┘

EXAMPLE: Rename column 'email' to 'email_address'

STEP 1: EXPAND (add new, keep old)
  ┌─────────────────────────────────────────────┐
  │  users                                      │
  │  ├── email           ← old (still used)    │
  │  └── email_address   ← new (added, synced)  │
  │                                             │
  │  Trigger: Copy email → email_address        │
  └─────────────────────────────────────────────┘


STEP 2: MIGRATE CODE
  - Deploy code that writes to BOTH columns
  - Reads from new column


STEP 3: BACKFILL
  - Update all existing rows: email_address = email


STEP 4: CONTRACT (remove old)
  - Deploy code that only uses email_address
  - Drop old column (once all code updated)
  ┌─────────────────────────────────────────────┐
  │  users                                      │
  │  └── email_address   ← only column         │
  └─────────────────────────────────────────────┘
```

### 8.3 Safe Migration Rules

| Do | Don't |
|----|-------|
| Add nullable columns | Drop columns in same release |
| Add tables | Rename columns in same release |
| Add indexes concurrently | Add NOT NULL without default |
| Backfill data separately | Change column types directly |
| Remove after code updated | Lock tables during deploy |

### 8.4 Migration Tools

| Tool | Language | Notes |
|------|----------|-------|
| **Flyway** | Java/multi | Version-based, widely used |
| **Liquibase** | Java/multi | XML/YAML based |
| **Knex** | Node.js | Migration + query builder |
| **TypeORM** | Node.js | ORM with migrations |
| **Alembic** | Python | SQLAlchemy migrations |
| **gh-ost** | Any (MySQL) | Online schema changes |
| **pg_repack** | PostgreSQL | Online table optimization |

---

## 9. Trade-offs Analysis

### 9.1 Decision Matrix

| Strategy | Complexity | Cost | Rollback Speed | Risk Reduction |
|----------|------------|------|----------------|----------------|
| **Feature Flags** | Low | Low | Instant | High |
| **Blue-Green** | Medium | High | Instant | High |
| **Rolling** | Medium | Low | Slow (re-roll) | Medium |
| **Canary** | High | Medium | Fast | Very High |
| **A/B Testing** | High | Medium | Fast | Medium |

### 9.2 For Q2 Context

| Strategy | Verdict | Reasoning |
|----------|---------|-----------|
| **Feature Flags** | **Recommended** | Low cost, high value, fits monolith |
| **Blue-Green** | Consider | Good if infrastructure supports |
| **Rolling** | Requires containers | Not applicable if single instance |
| **Canary** | Overkill | Need significant traffic to be useful |
| **A/B Testing** | Different purpose | Not a deployment strategy |

### 9.3 Combination Approach

These aren't mutually exclusive:

```
RECOMMENDED Q2 APPROACH:

┌───────────────────────────────────────────────────────────────┐
│                                                               │
│  Feature Flags (primary)                                      │
│  + Blue-Green or Rolling (infrastructure-level)              │
│                                                               │
│  WORKFLOW:                                                    │
│  1. Develop feature with flag (off)                          │
│  2. Deploy to production (flag still off)                    │
│  3. Enable flag for 10% (test with real traffic)             │
│  4. Expand to 100% (or rollback if issues)                   │
│  5. Remove flag once stable                                  │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

---

## 10. Application to Q2

### 10.1 Recommendation

**Primary Strategy:** Feature Flags

For Q2's constraints (modular monolith, 5 developers), feature flags provide the best value:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Q2 DEPLOYMENT STRATEGY                        │
└─────────────────────────────────────────────────────────────────┘

DEPLOY FLOW:

  Developer                    CI/CD                    Production
      │                          │                          │
      │  1. Commit (flag: off)   │                          │
      ├─────────────────────────▶│                          │
      │                          │  2. Test                 │
      │                          │  3. Build                │
      │                          │  4. Deploy ─────────────▶│
      │                          │                          │
      │                          │     Feature deployed     │
      │                          │     but DISABLED         │
      │                          │                          │
      │  5. Enable flag (10%)    │                          │
      ├─────────────────────────────────────────────────────▶│
      │                          │                          │
      │  6. Monitor for issues   │                          │
      │                          │                          │
      │  7. Enable flag (100%)   │                          │
      ├─────────────────────────────────────────────────────▶│
      │                          │                          │
      │  8. (Later) Remove flag  │                          │
      │                          │                          │
```

### 10.2 Implementation Approach

**Simple Feature Flag Service:**

```javascript
// feature-flags.js
const flags = {
  'new-checkout': {
    enabled: process.env.FF_NEW_CHECKOUT === 'true',
    rolloutPercentage: parseInt(process.env.FF_NEW_CHECKOUT_PERCENT || '0'),
  },
  'async-notifications': {
    enabled: process.env.FF_ASYNC_NOTIFICATIONS === 'true',
    rolloutPercentage: 100, // All or nothing
  },
};

function isEnabled(flagName, userId = null) {
  const flag = flags[flagName];
  if (!flag || !flag.enabled) return false;

  if (userId && flag.rolloutPercentage < 100) {
    const bucket = simpleHash(userId) % 100;
    return bucket < flag.rolloutPercentage;
  }

  return true;
}

// Usage
if (isEnabled('new-checkout', customerId)) {
  return newCheckoutFlow(order);
} else {
  return oldCheckoutFlow(order);
}
```

### 10.3 Flag Hygiene

To avoid flag debt:

| Rule | Practice |
|------|----------|
| **Document** | Each flag has owner, purpose, cleanup date |
| **Limit** | Maximum 10-15 active flags |
| **Cleanup** | Remove flag within 2 weeks of full rollout |
| **Review** | Monthly flag audit |

### 10.4 Infrastructure Deployment

For the actual deployment process, if not already using:

**If Single Server:**
- Basic: Stop, deploy, start (brief downtime)
- Better: PM2 or similar with graceful restart

**If Multiple Servers / Containers:**
- Rolling deployment via Kubernetes, Docker Swarm, or ECS
- Load balancer manages traffic during roll

### 10.5 Database Migration Strategy

Apply Expand-Contract for schema changes:

```
EXAMPLE: Adding 'preferred_payment_method' to customers

STEP 1: Add nullable column (deploy 1)
  ALTER TABLE customers
  ADD COLUMN preferred_payment_method VARCHAR(50);

STEP 2: Code writes to new column (deploy 2)
  - New code: writes preferred_payment_method
  - Old code: ignores it (nullable)

STEP 3: Backfill existing data (background job)
  UPDATE customers
  SET preferred_payment_method = 'card'
  WHERE preferred_payment_method IS NULL;

STEP 4: Add NOT NULL if needed (deploy 3, after backfill)
  ALTER TABLE customers
  ALTER COLUMN preferred_payment_method SET NOT NULL;
```

---

## 11. References

### Articles

| Article | Source |
|---------|--------|
| Blue-Green Deployments | https://martinfowler.com/bliki/BlueGreenDeployment.html |
| Canary Releases | https://martinfowler.com/bliki/CanaryRelease.html |
| Feature Toggles | https://martinfowler.com/articles/feature-toggles.html |
| Evolutionary Database Design | https://martinfowler.com/articles/evodb.html |

### Tools

| Tool | Purpose | Link |
|------|---------|------|
| **LaunchDarkly** | Feature flags (SaaS) | https://launchdarkly.com/ |
| **Unleash** | Feature flags (open source) | https://www.getunleash.io/ |
| **Flagsmith** | Feature flags (open source) | https://flagsmith.com/ |
| **Flyway** | Database migrations | https://flywaydb.org/ |

### Books

| Book | Author | Relevance |
|------|--------|-----------|
| Continuous Delivery | Jez Humble & David Farley | Deployment pipelines |
| Release It! | Michael Nygard | Production readiness |

---

## Document Summary

For Q2's disruptive deployment problem:

1. **Implement Feature Flags** to decouple deployment from release
2. **Use environment-based flags** (simple) or database-backed (flexible)
3. **Gradual rollout** - enable for percentage of users before full rollout
4. **Maintain flag hygiene** - document, limit, and clean up flags
5. **Apply Expand-Contract** for database schema changes

This reduces deployment risk significantly while keeping the monolith simple. More sophisticated deployment infrastructure (blue-green, canary) can be added later if needed.
