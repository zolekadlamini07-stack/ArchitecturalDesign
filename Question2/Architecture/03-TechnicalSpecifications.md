# Technical Specifications: Q2 Implementation

## Document Purpose

This document provides detailed technical specifications for implementing the Q2 architecture evolution. It covers configuration, code patterns, and operational details for each change.

**Note:** We are not redesigning components. The existing Q1 components (Order, Customer, Restaurant, etc.) remain. We are adding infrastructure and modifying how they interact.

---

## Table of Contents

1. [Infrastructure Setup](#1-infrastructure-setup)
2. [RabbitMQ Implementation](#2-rabbitmq-implementation)
3. [Redis Cache Implementation](#3-redis-cache-implementation)
4. [PostgreSQL Read Replica](#4-postgresql-read-replica)
5. [Feature Flags Implementation](#5-feature-flags-implementation)
6. [Database Optimization](#6-database-optimization)
7. [Configuration Management](#7-configuration-management)
8. [Monitoring and Observability](#8-monitoring-and-observability)
9. [Deployment Updates](#9-deployment-updates)

---

## 1. Infrastructure Setup

### 1.1 Infrastructure Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Q2 INFRASTRUCTURE                                        │
└─────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│  Application Tier                                                            │
│                                                                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐              │
│  │   API Server    │  │ Notification    │  │  Notification   │              │
│  │   (main app)    │  │ Worker (cust)   │  │  Worker (rest)  │              │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘              │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
         │                      │                      │
         │                      │                      │
         ▼                      ▼                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  Data Tier                                                                   │
│                                                                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐              │
│  │   PostgreSQL    │  │   PostgreSQL    │  │     Redis       │              │
│  │    Primary      │─▶│    Replica      │  │                 │              │
│  │                 │  │                 │  │                 │              │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘              │
│                                                                              │
│  ┌─────────────────┐                                                        │
│  │    RabbitMQ     │                                                        │
│  │                 │                                                        │
│  └─────────────────┘                                                        │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Resource Requirements

| Component | CPU | Memory | Storage | Notes |
|-----------|-----|--------|---------|-------|
| API Server | 2 cores | 4 GB | 20 GB | Same as Q1, possibly scaled |
| PostgreSQL Primary | 2 cores | 4 GB | 100 GB | Existing, may need more |
| PostgreSQL Replica | 2 cores | 4 GB | 100 GB | New, same as primary |
| Redis | 1 core | 2 GB | 1 GB | New |
| RabbitMQ | 1 core | 1 GB | 10 GB | New |
| Notification Workers | 1 core | 1 GB | 10 GB | New (can be 1-2 instances) |

### 1.3 Network Configuration

| Service | Port | Access |
|---------|------|--------|
| API Server | 3000/443 | Public (via load balancer) |
| PostgreSQL Primary | 5432 | Internal only |
| PostgreSQL Replica | 5432 | Internal only |
| Redis | 6379 | Internal only |
| RabbitMQ | 5672 | Internal only |
| RabbitMQ Management | 15672 | Internal (admin access) |

---

## 2. RabbitMQ Implementation

### 2.1 RabbitMQ Setup

**Docker Compose (development):**
```yaml
# docker-compose.yml
services:
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: app
      RABBITMQ_DEFAULT_PASS: secret
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq

volumes:
  rabbitmq_data:
```

**Production:** Use managed service (CloudAMQP, AWS MQ) or deploy with clustering.

### 2.2 Exchange and Queue Configuration

```javascript
// rabbitmq-setup.js
const amqp = require('amqplib');

async function setupRabbitMQ() {
  const connection = await amqp.connect(process.env.RABBITMQ_URL);
  const channel = await connection.createChannel();

  // === Notification Exchange ===
  await channel.assertExchange('notifications', 'direct', {
    durable: true
  });

  // Customer notifications queue
  await channel.assertQueue('customer_notifications', {
    durable: true,
    arguments: {
      'x-dead-letter-exchange': '',
      'x-dead-letter-routing-key': 'customer_notifications_dlq'
    }
  });

  // Restaurant notifications queue
  await channel.assertQueue('restaurant_notifications', {
    durable: true,
    arguments: {
      'x-dead-letter-exchange': '',
      'x-dead-letter-routing-key': 'restaurant_notifications_dlq'
    }
  });

  // Dead letter queues
  await channel.assertQueue('customer_notifications_dlq', { durable: true });
  await channel.assertQueue('restaurant_notifications_dlq', { durable: true });

  // Bindings
  const customerEvents = ['order_placed', 'order_accepted', 'order_rejected',
                          'driver_assigned', 'out_for_delivery', 'order_delivered'];
  const restaurantEvents = ['order_placed', 'order_cancelled'];

  for (const event of customerEvents) {
    await channel.bindQueue('customer_notifications', 'notifications', event);
  }

  for (const event of restaurantEvents) {
    await channel.bindQueue('restaurant_notifications', 'notifications', event);
  }

  // === Cache Invalidation Exchange ===
  await channel.assertExchange('cache_invalidation', 'fanout', {
    durable: true
  });

  await channel.assertQueue('cache_invalidation_queue', { durable: true });
  await channel.bindQueue('cache_invalidation_queue', 'cache_invalidation', '');

  console.log('RabbitMQ setup complete');
  await channel.close();
  await connection.close();
}
```

### 2.3 Publisher Module

```javascript
// infrastructure/messaging/publisher.js
const amqp = require('amqplib');

class MessagePublisher {
  constructor() {
    this.connection = null;
    this.channel = null;
  }

  async connect() {
    this.connection = await amqp.connect(process.env.RABBITMQ_URL);
    this.channel = await this.connection.createChannel();

    // Ensure exchanges exist
    await this.channel.assertExchange('notifications', 'direct', { durable: true });
    await this.channel.assertExchange('cache_invalidation', 'fanout', { durable: true });
  }

  async publishNotification(event) {
    const routingKey = event.type.toLowerCase();
    const message = Buffer.from(JSON.stringify({
      ...event,
      timestamp: new Date().toISOString(),
      id: this.generateId()
    }));

    this.channel.publish('notifications', routingKey, message, {
      persistent: true,
      contentType: 'application/json'
    });
  }

  async publishCacheInvalidation(event) {
    const message = Buffer.from(JSON.stringify({
      ...event,
      timestamp: new Date().toISOString()
    }));

    this.channel.publish('cache_invalidation', '', message, {
      persistent: true,
      contentType: 'application/json'
    });
  }

  generateId() {
    return `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
  }

  async close() {
    await this.channel?.close();
    await this.connection?.close();
  }
}

module.exports = new MessagePublisher();
```

### 2.4 Consumer/Worker Module

```javascript
// workers/notification-worker.js
const amqp = require('amqplib');
const pushService = require('../infrastructure/push-service');
const logger = require('../infrastructure/logger');

class NotificationWorker {
  constructor(queueName) {
    this.queueName = queueName;
    this.connection = null;
    this.channel = null;
    this.maxRetries = 3;
  }

  async start() {
    this.connection = await amqp.connect(process.env.RABBITMQ_URL);
    this.channel = await this.connection.createChannel();

    // Process one message at a time
    await this.channel.prefetch(1);

    await this.channel.consume(this.queueName, async (msg) => {
      await this.processMessage(msg);
    }, { noAck: false });

    logger.info(`Worker started for queue: ${this.queueName}`);
  }

  async processMessage(msg) {
    const event = JSON.parse(msg.content.toString());
    const retryCount = (msg.properties.headers?.['x-retry-count'] || 0);

    try {
      await this.handleEvent(event);
      this.channel.ack(msg);
      logger.info(`Processed event: ${event.type}`, { eventId: event.id });

    } catch (error) {
      logger.error(`Failed to process event: ${event.type}`, {
        eventId: event.id,
        error: error.message,
        retryCount
      });

      if (retryCount < this.maxRetries) {
        // Republish with retry count
        this.channel.publish('', this.queueName, msg.content, {
          persistent: true,
          headers: { 'x-retry-count': retryCount + 1 }
        });
        this.channel.ack(msg);
      } else {
        // Send to dead letter queue
        this.channel.nack(msg, false, false);
        logger.error(`Event sent to DLQ after ${this.maxRetries} retries`, {
          eventId: event.id
        });
      }
    }
  }

  async handleEvent(event) {
    switch (event.type) {
      case 'order_placed':
        await this.sendOrderPlacedNotification(event);
        break;
      case 'order_accepted':
        await this.sendOrderAcceptedNotification(event);
        break;
      case 'order_rejected':
        await this.sendOrderRejectedNotification(event);
        break;
      case 'driver_assigned':
        await this.sendDriverAssignedNotification(event);
        break;
      case 'out_for_delivery':
        await this.sendOutForDeliveryNotification(event);
        break;
      case 'order_delivered':
        await this.sendOrderDeliveredNotification(event);
        break;
      default:
        logger.warn(`Unknown event type: ${event.type}`);
    }
  }

  async sendOrderPlacedNotification(event) {
    await pushService.send(event.customerId, {
      title: 'Order Received',
      body: 'Your order has been sent to the restaurant.',
      data: { orderId: event.orderId }
    });
  }

  async sendOrderAcceptedNotification(event) {
    await pushService.send(event.customerId, {
      title: 'Order Confirmed',
      body: `Your order is being prepared. Estimated delivery: ${event.estimatedTime}`,
      data: { orderId: event.orderId }
    });
  }

  // ... other notification methods
}

// Start customer notification worker
if (require.main === module) {
  const worker = new NotificationWorker('customer_notifications');
  worker.start().catch(console.error);
}

module.exports = NotificationWorker;
```

### 2.5 Integration with Order Service

```javascript
// modules/order/application/services/OrderService.js (Q2 version)
const messagePublisher = require('../../../../infrastructure/messaging/publisher');

class OrderService {
  constructor(orderRepository, paymentService) {
    this.orderRepository = orderRepository;
    this.paymentService = paymentService;
  }

  async placeOrder(orderData) {
    // 1. Validate and create order
    const order = Order.create(orderData);

    // 2. Save to database
    const savedOrder = await this.orderRepository.save(order);

    // 3. Publish notification event (async, non-blocking)
    await messagePublisher.publishNotification({
      type: 'order_placed',
      orderId: savedOrder.id,
      customerId: savedOrder.customerId,
      restaurantId: savedOrder.restaurantId
    });

    // 4. Return immediately (don't wait for notification)
    return savedOrder;
  }

  async acceptOrder(orderId, estimatedTime) {
    const order = await this.orderRepository.findById(orderId);
    order.accept(estimatedTime);
    await this.orderRepository.save(order);

    // Process payment
    await this.paymentService.process(order);

    // Async notification
    await messagePublisher.publishNotification({
      type: 'order_accepted',
      orderId: order.id,
      customerId: order.customerId,
      estimatedTime
    });

    return order;
  }
}
```

---

## 3. Redis Cache Implementation

### 3.1 Redis Setup

**Docker Compose (development):**
```yaml
services:
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    command: redis-server --appendonly yes

volumes:
  redis_data:
```

**Production:** Use managed Redis (AWS ElastiCache, Azure Cache for Redis).

### 3.2 Cache Service

```javascript
// infrastructure/cache/CacheService.js
const Redis = require('ioredis');

class CacheService {
  constructor() {
    this.client = new Redis({
      host: process.env.REDIS_HOST,
      port: process.env.REDIS_PORT || 6379,
      password: process.env.REDIS_PASSWORD,
      retryDelayOnFailover: 100,
      maxRetriesPerRequest: 3
    });

    this.client.on('error', (err) => {
      console.error('Redis connection error:', err);
    });
  }

  async get(key) {
    try {
      const value = await this.client.get(key);
      return value ? JSON.parse(value) : null;
    } catch (error) {
      console.error('Cache get error:', error);
      return null; // Fail open - return null on cache error
    }
  }

  async set(key, value, ttlSeconds) {
    try {
      await this.client.setex(key, ttlSeconds, JSON.stringify(value));
    } catch (error) {
      console.error('Cache set error:', error);
      // Don't throw - cache failures shouldn't break the app
    }
  }

  async del(key) {
    try {
      await this.client.del(key);
    } catch (error) {
      console.error('Cache delete error:', error);
    }
  }

  async delPattern(pattern) {
    try {
      const keys = await this.client.keys(pattern);
      if (keys.length > 0) {
        await this.client.del(...keys);
      }
    } catch (error) {
      console.error('Cache delete pattern error:', error);
    }
  }
}

module.exports = new CacheService();
```

### 3.3 Cached Restaurant Service

```javascript
// modules/restaurant/application/services/RestaurantService.js (Q2)
const cacheService = require('../../../../infrastructure/cache/CacheService');
const messagePublisher = require('../../../../infrastructure/messaging/publisher');

class RestaurantService {
  constructor(restaurantRepository) {
    this.repository = restaurantRepository;
    this.CACHE_TTL = {
      RESTAURANT_LIST: 300,    // 5 minutes
      RESTAURANT_DETAIL: 300,  // 5 minutes
      MENU: 120,               // 2 minutes
      AVAILABILITY: 30         // 30 seconds
    };
  }

  async getRestaurantsByCity(cityId) {
    const cacheKey = `restaurants:city:${cityId}`;

    // Try cache first
    const cached = await cacheService.get(cacheKey);
    if (cached) {
      return cached;
    }

    // Cache miss - fetch from database
    const restaurants = await this.repository.findByCity(cityId);

    // Store in cache
    await cacheService.set(cacheKey, restaurants, this.CACHE_TTL.RESTAURANT_LIST);

    return restaurants;
  }

  async getRestaurantById(id) {
    const cacheKey = `restaurant:${id}`;

    const cached = await cacheService.get(cacheKey);
    if (cached) {
      return cached;
    }

    const restaurant = await this.repository.findById(id);
    if (restaurant) {
      await cacheService.set(cacheKey, restaurant, this.CACHE_TTL.RESTAURANT_DETAIL);
    }

    return restaurant;
  }

  async updateRestaurant(id, data) {
    const restaurant = await this.repository.update(id, data);

    // Invalidate cache
    await cacheService.del(`restaurant:${id}`);
    await cacheService.delPattern(`restaurants:city:*`);

    // Publish cache invalidation event
    await messagePublisher.publishCacheInvalidation({
      type: 'restaurant_updated',
      restaurantId: id
    });

    return restaurant;
  }
}
```

### 3.4 Cached Menu Service

```javascript
// modules/menu/application/services/MenuService.js (Q2)
const cacheService = require('../../../../infrastructure/cache/CacheService');
const messagePublisher = require('../../../../infrastructure/messaging/publisher');

class MenuService {
  constructor(menuRepository) {
    this.repository = menuRepository;
  }

  async getMenuByRestaurant(restaurantId) {
    const cacheKey = `menu:${restaurantId}`;

    const cached = await cacheService.get(cacheKey);
    if (cached) {
      return cached;
    }

    const menu = await this.repository.findByRestaurantId(restaurantId);
    await cacheService.set(cacheKey, menu, 120); // 2 minutes

    return menu;
  }

  async updateItemAvailability(itemId, available) {
    const item = await this.repository.updateAvailability(itemId, available);

    // Short TTL cache - also event-driven invalidation
    await cacheService.del(`menu:${item.restaurantId}`);

    await messagePublisher.publishCacheInvalidation({
      type: 'menu_updated',
      restaurantId: item.restaurantId,
      itemId: item.id
    });

    return item;
  }
}
```

### 3.5 Cache Invalidation Worker

```javascript
// workers/cache-invalidation-worker.js
const amqp = require('amqplib');
const cacheService = require('../infrastructure/cache/CacheService');
const logger = require('../infrastructure/logger');

async function startWorker() {
  const connection = await amqp.connect(process.env.RABBITMQ_URL);
  const channel = await connection.createChannel();

  await channel.consume('cache_invalidation_queue', async (msg) => {
    const event = JSON.parse(msg.content.toString());

    try {
      switch (event.type) {
        case 'restaurant_updated':
          await cacheService.del(`restaurant:${event.restaurantId}`);
          await cacheService.delPattern(`restaurants:city:*`);
          break;

        case 'menu_updated':
          await cacheService.del(`menu:${event.restaurantId}`);
          break;

        default:
          logger.warn(`Unknown cache invalidation event: ${event.type}`);
      }

      channel.ack(msg);
      logger.info(`Cache invalidated for: ${event.type}`);

    } catch (error) {
      logger.error('Cache invalidation error:', error);
      channel.nack(msg, false, true); // Requeue
    }
  }, { noAck: false });

  logger.info('Cache invalidation worker started');
}

startWorker().catch(console.error);
```

---

## 4. PostgreSQL Read Replica

### 4.1 Replica Setup (AWS RDS Example)

For AWS RDS:
1. Go to RDS console
2. Select primary instance
3. Actions → Create read replica
4. Configure instance size (same as primary recommended)
5. Note the replica endpoint

### 4.2 Database Connection Configuration

```javascript
// infrastructure/database/index.js
const { Pool } = require('pg');

// Primary connection (reads and writes)
const primaryPool = new Pool({
  host: process.env.DB_PRIMARY_HOST,
  port: process.env.DB_PORT || 5432,
  database: process.env.DB_NAME,
  user: process.env.DB_USER,
  password: process.env.DB_PASSWORD,
  max: 20,                    // Connection pool size
  idleTimeoutMillis: 30000,
  connectionTimeoutMillis: 2000,
});

// Replica connection (reads only)
const replicaPool = new Pool({
  host: process.env.DB_REPLICA_HOST,
  port: process.env.DB_PORT || 5432,
  database: process.env.DB_NAME,
  user: process.env.DB_USER,
  password: process.env.DB_PASSWORD,
  max: 10,                    // Smaller pool for reports
  idleTimeoutMillis: 30000,
  connectionTimeoutMillis: 2000,
});

module.exports = {
  primary: primaryPool,
  replica: replicaPool,

  // Helper for query routing
  query: async (sql, params, options = {}) => {
    const pool = options.useReplica ? replicaPool : primaryPool;
    return pool.query(sql, params);
  }
};
```

### 4.3 Repository Pattern with Replica

```javascript
// modules/order/infrastructure/PostgresOrderRepository.js
const db = require('../../../../infrastructure/database');

class PostgresOrderRepository {
  // Writes always go to primary
  async save(order) {
    const result = await db.primary.query(
      `INSERT INTO orders (id, customer_id, restaurant_id, state, total_amount, created_at)
       VALUES ($1, $2, $3, $4, $5, $6)
       RETURNING *`,
      [order.id, order.customerId, order.restaurantId, order.state, order.totalAmount, new Date()]
    );
    return this.mapToEntity(result.rows[0]);
  }

  // Single order lookup - use primary for latest state
  async findById(id) {
    const result = await db.primary.query(
      'SELECT * FROM orders WHERE id = $1',
      [id]
    );
    return result.rows[0] ? this.mapToEntity(result.rows[0]) : null;
  }

  // Order history - can use replica (slight lag OK)
  async findByCustomerId(customerId, options = {}) {
    const result = await db.query(
      'SELECT * FROM orders WHERE customer_id = $1 ORDER BY created_at DESC LIMIT $2',
      [customerId, options.limit || 50],
      { useReplica: true }  // Route to replica
    );
    return result.rows.map(this.mapToEntity);
  }
}
```

### 4.4 Reporting Repository

```javascript
// modules/reporting/infrastructure/PostgresReportingRepository.js
const db = require('../../../../infrastructure/database');

class PostgresReportingRepository {
  // All reporting queries use replica
  async getRevenueByRestaurant(startDate, endDate) {
    const result = await db.replica.query(`
      SELECT
        restaurant_id,
        SUM(total_amount) as revenue,
        COUNT(*) as order_count
      FROM orders
      WHERE created_at BETWEEN $1 AND $2
        AND state = 'COMPLETED'
      GROUP BY restaurant_id
    `, [startDate, endDate]);

    return result.rows;
  }

  async getDailyOrderCounts(restaurantId, days = 30) {
    const result = await db.replica.query(`
      SELECT
        DATE(created_at) as order_date,
        COUNT(*) as order_count,
        SUM(total_amount) as revenue
      FROM orders
      WHERE restaurant_id = $1
        AND created_at > NOW() - INTERVAL '${days} days'
      GROUP BY DATE(created_at)
      ORDER BY order_date DESC
    `, [restaurantId]);

    return result.rows;
  }

  async getDeliveryPerformance(restaurantId) {
    const result = await db.replica.query(`
      SELECT
        AVG(EXTRACT(EPOCH FROM (d.delivered_at - o.created_at))/60) as avg_delivery_minutes,
        COUNT(*) as total_deliveries
      FROM deliveries d
      JOIN orders o ON d.order_id = o.id
      WHERE o.restaurant_id = $1
        AND d.state = 'DELIVERED'
        AND o.created_at > NOW() - INTERVAL '7 days'
    `, [restaurantId]);

    return result.rows[0];
  }
}
```

### 4.5 Materialized Views

```sql
-- Create on replica database

-- Daily revenue summary
CREATE MATERIALIZED VIEW mv_daily_revenue AS
SELECT
  restaurant_id,
  DATE(created_at) as order_date,
  COUNT(*) as order_count,
  SUM(total_amount) as revenue,
  AVG(total_amount) as avg_order_value
FROM orders
WHERE state = 'COMPLETED'
GROUP BY restaurant_id, DATE(created_at);

CREATE UNIQUE INDEX ON mv_daily_revenue (restaurant_id, order_date);

-- Order status summary (real-time-ish)
CREATE MATERIALIZED VIEW mv_order_status_summary AS
SELECT
  restaurant_id,
  state,
  COUNT(*) as count
FROM orders
WHERE created_at > NOW() - INTERVAL '24 hours'
GROUP BY restaurant_id, state;

CREATE UNIQUE INDEX ON mv_order_status_summary (restaurant_id, state);

-- Refresh schedule (via cron or pg_cron)
-- Daily revenue: once per hour
-- Order status: every 15 minutes
```

---

## 5. Feature Flags Implementation

### 5.1 Database Schema

```sql
CREATE TABLE feature_flags (
  name VARCHAR(100) PRIMARY KEY,
  enabled BOOLEAN DEFAULT false,
  rollout_percentage INTEGER DEFAULT 0 CHECK (rollout_percentage BETWEEN 0 AND 100),
  description TEXT,
  owner VARCHAR(100),
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);

-- Initial flags
INSERT INTO feature_flags (name, enabled, rollout_percentage, description, owner) VALUES
  ('async_notifications', false, 0, 'Use RabbitMQ for notifications', 'team'),
  ('redis_caching', false, 0, 'Enable Redis caching for restaurant/menu', 'team'),
  ('new_checkout_flow', false, 0, 'New checkout UI experiment', 'team');
```

### 5.2 Feature Flag Service

```javascript
// infrastructure/feature-flags/FeatureFlagService.js
const db = require('../database');
const cacheService = require('../cache/CacheService');

class FeatureFlagService {
  async isEnabled(flagName, userId = null) {
    const flag = await this.getFlag(flagName);

    if (!flag || !flag.enabled) {
      return false;
    }

    // If percentage rollout and we have userId
    if (userId && flag.rollout_percentage < 100) {
      const bucket = this.hashToBucket(userId);
      return bucket < flag.rollout_percentage;
    }

    return flag.enabled;
  }

  async getFlag(name) {
    const cacheKey = `feature_flag:${name}`;

    // Try cache first
    let flag = await cacheService.get(cacheKey);
    if (flag) {
      return flag;
    }

    // Fetch from database
    const result = await db.primary.query(
      'SELECT * FROM feature_flags WHERE name = $1',
      [name]
    );

    flag = result.rows[0] || null;

    // Cache for 1 minute
    if (flag) {
      await cacheService.set(cacheKey, flag, 60);
    }

    return flag;
  }

  hashToBucket(userId) {
    // Simple consistent hashing
    let hash = 0;
    const str = String(userId);
    for (let i = 0; i < str.length; i++) {
      hash = ((hash << 5) - hash) + str.charCodeAt(i);
      hash |= 0;
    }
    return Math.abs(hash) % 100;
  }

  // Admin functions
  async setFlag(name, enabled, rolloutPercentage = null) {
    const result = await db.primary.query(`
      UPDATE feature_flags
      SET enabled = $2,
          rollout_percentage = COALESCE($3, rollout_percentage),
          updated_at = NOW()
      WHERE name = $1
      RETURNING *
    `, [name, enabled, rolloutPercentage]);

    // Invalidate cache
    await cacheService.del(`feature_flag:${name}`);

    return result.rows[0];
  }
}

module.exports = new FeatureFlagService();
```

### 5.3 Usage Example

```javascript
// In Order Service
const featureFlags = require('../../../../infrastructure/feature-flags/FeatureFlagService');
const messagePublisher = require('../../../../infrastructure/messaging/publisher');
const notificationService = require('./NotificationService'); // Old sync service

class OrderService {
  async placeOrder(orderData) {
    const order = Order.create(orderData);
    const savedOrder = await this.orderRepository.save(order);

    // Feature flag: async vs sync notifications
    if (await featureFlags.isEnabled('async_notifications')) {
      // Q2: Async via RabbitMQ
      await messagePublisher.publishNotification({
        type: 'order_placed',
        orderId: savedOrder.id,
        customerId: savedOrder.customerId,
        restaurantId: savedOrder.restaurantId
      });
    } else {
      // Q1: Sync notifications (old way)
      await notificationService.notifyOrderPlaced(savedOrder);
    }

    return savedOrder;
  }
}
```

---

## 6. Database Optimization

### 6.1 Index Additions

```sql
-- Orders table
CREATE INDEX CONCURRENTLY idx_orders_restaurant_id ON orders(restaurant_id);
CREATE INDEX CONCURRENTLY idx_orders_customer_id ON orders(customer_id);
CREATE INDEX CONCURRENTLY idx_orders_state ON orders(state);
CREATE INDEX CONCURRENTLY idx_orders_created_at ON orders(created_at);
CREATE INDEX CONCURRENTLY idx_orders_restaurant_created ON orders(restaurant_id, created_at);

-- Order items
CREATE INDEX CONCURRENTLY idx_order_items_order_id ON order_items(order_id);

-- Menu items
CREATE INDEX CONCURRENTLY idx_menu_items_restaurant_id ON menu_items(restaurant_id);
CREATE INDEX CONCURRENTLY idx_menu_items_available ON menu_items(restaurant_id, is_available);

-- Deliveries
CREATE INDEX CONCURRENTLY idx_deliveries_order_id ON deliveries(order_id);
CREATE INDEX CONCURRENTLY idx_deliveries_driver_id ON deliveries(driver_id);
CREATE INDEX CONCURRENTLY idx_deliveries_state ON deliveries(state);

-- Driver shifts
CREATE INDEX CONCURRENTLY idx_driver_shifts_driver_id ON driver_shifts(driver_id);
CREATE INDEX CONCURRENTLY idx_driver_shifts_active ON driver_shifts(driver_id, is_active);
```

### 6.2 Query Analysis Setup

```sql
-- Enable query statistics
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

-- View slow queries
SELECT
  query,
  calls,
  mean_time,
  total_time
FROM pg_stat_statements
ORDER BY total_time DESC
LIMIT 20;
```

---

## 7. Configuration Management

### 7.1 Environment Variables

```bash
# .env.example

# Application
NODE_ENV=production
PORT=3000

# PostgreSQL Primary
DB_PRIMARY_HOST=primary.db.example.com
DB_PORT=5432
DB_NAME=food_delivery
DB_USER=app
DB_PASSWORD=secret

# PostgreSQL Replica
DB_REPLICA_HOST=replica.db.example.com

# Redis
REDIS_HOST=redis.example.com
REDIS_PORT=6379
REDIS_PASSWORD=secret

# RabbitMQ
RABBITMQ_URL=amqp://user:pass@rabbitmq.example.com:5672

# Push Notifications
FIREBASE_PROJECT_ID=your-project
FIREBASE_PRIVATE_KEY=...

# Feature Flags
FEATURE_FLAGS_REFRESH_INTERVAL=60000
```

---

## 8. Monitoring and Observability

### 8.1 Key Metrics to Monitor

| Component | Metric | Alert Threshold |
|-----------|--------|-----------------|
| **RabbitMQ** | Queue depth | > 1000 |
| **RabbitMQ** | Consumer count | = 0 |
| **RabbitMQ** | Dead letter count | > 50 |
| **Redis** | Memory usage | > 80% |
| **Redis** | Hit rate | < 80% |
| **PostgreSQL** | Replication lag | > 10 seconds |
| **PostgreSQL** | Connection count | > 80% of max |
| **API** | Error rate | > 1% |
| **API** | P95 latency | > 500ms |

### 8.2 Health Check Endpoint

```javascript
// routes/health.js
const db = require('../infrastructure/database');
const cacheService = require('../infrastructure/cache/CacheService');
const amqp = require('amqplib');

router.get('/health', async (req, res) => {
  const checks = {};

  // Database primary
  try {
    await db.primary.query('SELECT 1');
    checks.database_primary = 'ok';
  } catch (e) {
    checks.database_primary = 'error';
  }

  // Database replica
  try {
    await db.replica.query('SELECT 1');
    checks.database_replica = 'ok';
  } catch (e) {
    checks.database_replica = 'error';
  }

  // Redis
  try {
    await cacheService.client.ping();
    checks.redis = 'ok';
  } catch (e) {
    checks.redis = 'error';
  }

  // RabbitMQ
  try {
    const conn = await amqp.connect(process.env.RABBITMQ_URL);
    await conn.close();
    checks.rabbitmq = 'ok';
  } catch (e) {
    checks.rabbitmq = 'error';
  }

  const healthy = Object.values(checks).every(v => v === 'ok');

  res.status(healthy ? 200 : 503).json({
    status: healthy ? 'healthy' : 'unhealthy',
    checks
  });
});
```

---

## 9. Deployment Updates

### 9.1 Updated Deployment Process

```
┌─────────────────────────────────────────────────────────────────┐
│                    Q2 DEPLOYMENT PROCESS                         │
└─────────────────────────────────────────────────────────────────┘

1. Pre-deploy checks:
   □ All tests passing
   □ New features behind feature flags
   □ Database migrations reviewed

2. Infrastructure deploy (if needed):
   □ Redis/RabbitMQ config updates
   □ Database migrations (expand phase)

3. Application deploy:
   □ Deploy API servers (rolling)
   □ Deploy notification workers

4. Post-deploy:
   □ Verify health checks
   □ Monitor error rates

5. Feature rollout (separate from deploy):
   □ Enable flag for 10%
   □ Monitor for issues
   □ Expand to 100%
```

### 9.2 Docker Compose (Full Stack)

```yaml
# docker-compose.yml (development)
version: '3.8'

services:
  api:
    build: .
    ports:
      - "3000:3000"
    environment:
      - NODE_ENV=development
      - DB_PRIMARY_HOST=postgres
      - REDIS_HOST=redis
      - RABBITMQ_URL=amqp://rabbitmq
    depends_on:
      - postgres
      - redis
      - rabbitmq

  notification-worker:
    build: .
    command: node workers/notification-worker.js
    environment:
      - NODE_ENV=development
      - DB_PRIMARY_HOST=postgres
      - REDIS_HOST=redis
      - RABBITMQ_URL=amqp://rabbitmq
    depends_on:
      - rabbitmq

  postgres:
    image: postgres:14
    environment:
      POSTGRES_DB: food_delivery
      POSTGRES_USER: app
      POSTGRES_PASSWORD: secret
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq

volumes:
  postgres_data:
  redis_data:
  rabbitmq_data:
```

---

## Document Summary

This document provides implementation details for Q2 evolution:

| Component | Key Specifications |
|-----------|-------------------|
| **RabbitMQ** | Direct exchange, durable queues, dead letter handling |
| **Redis** | Cache-aside pattern, TTL-based + event invalidation |
| **Read Replica** | Replica for reports, materialized views |
| **Feature Flags** | Database-backed, cached, percentage rollout |
| **Database** | Comprehensive indexing, query monitoring |

All changes are additive - the existing Q1 code structure remains, with new infrastructure integrated via clean interfaces.

---

*Next: Update diagrams to reflect Q2 architecture*
