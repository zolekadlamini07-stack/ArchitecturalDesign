# Message Queues and Async Processing Research

## Document Purpose

This document provides research on message queues and asynchronous processing patterns, with a deep dive into RabbitMQ (our chosen solution for Q2). The goal is to understand how to implement async notifications and potentially other async processing.

---

## Table of Contents

1. [The Problem We're Solving](#1-the-problem-were-solving)
2. [Async Processing Fundamentals](#2-async-processing-fundamentals)
3. [Message Queue Concepts](#3-message-queue-concepts)
4. [RabbitMQ Deep Dive](#4-rabbitmq-deep-dive)
5. [Integration Patterns](#5-integration-patterns)
6. [Error Handling and Reliability](#6-error-handling-and-reliability)
7. [Monitoring and Operations](#7-monitoring-and-operations)
8. [Alternative Technologies](#8-alternative-technologies)
9. [Application to Q2](#9-application-to-q2)
10. [References](#10-references)

---

## 1. The Problem We're Solving

**Symptom:** Notifications are slowing down certain operations.

**Q2 Context:**
- When order is placed → notification sent → response returned
- Push notification services have latency (100ms - 500ms+)
- User waits for notification to complete
- Failure in notification could fail the order

**Goal:** Decouple notification sending from request processing. User gets immediate response; notifications happen in background.

---

## 2. Async Processing Fundamentals

### 2.1 Synchronous vs Asynchronous

```
┌─────────────────────────────────────────────────────────────────┐
│                 SYNCHRONOUS PROCESSING                          │
└─────────────────────────────────────────────────────────────────┘

User Request                                            Response
    │                                                      ▲
    │  ┌────────────────────────────────────────────────┐  │
    └─▶│ Place Order ─▶ Save DB ─▶ Send Notification ─┼──┘
       └────────────────────────────────────────────────┘
                              │
                        User waits for
                        entire chain


┌─────────────────────────────────────────────────────────────────┐
│                ASYNCHRONOUS PROCESSING                          │
└─────────────────────────────────────────────────────────────────┘

User Request                          Response (fast!)
    │                                      ▲
    │  ┌────────────────────────────────┐  │
    └─▶│ Place Order ─▶ Save DB ─▶ Queue│──┘
       └────────────────────────────────┘
                              │
                        User doesn't wait
                              │
                              ▼
                  ┌─────────────────────┐
                  │  Background Worker  │
                  │  - Read from queue  │
                  │  - Send notification│
                  │  - (retries if fail)│
                  └─────────────────────┘
```

### 2.2 When to Use Async

| Scenario | Sync or Async? |
|----------|----------------|
| **Payment processing** | Sync (critical, need result) |
| **Order creation** | Sync (need to confirm success) |
| **Notifications** | Async (not critical path) |
| **Email sending** | Async (slow, can retry) |
| **Analytics events** | Async (non-blocking) |
| **PDF generation** | Async (slow, background) |

### 2.3 Benefits of Async Processing

| Benefit | Description |
|---------|-------------|
| **Lower latency** | User doesn't wait for slow operations |
| **Fault isolation** | Notification failure doesn't fail order |
| **Scalability** | Workers can scale independently |
| **Resilience** | Built-in retry mechanisms |
| **Load leveling** | Smooth out traffic spikes |

---

## 3. Message Queue Concepts

### 3.1 Core Components

```
┌─────────────────────────────────────────────────────────────────┐
│                    MESSAGE QUEUE ARCHITECTURE                    │
└─────────────────────────────────────────────────────────────────┘

┌───────────┐        ┌─────────────┐        ┌───────────┐
│           │        │             │        │           │
│  Producer │───────▶│    Queue    │───────▶│  Consumer │
│           │ publish│   (Broker)  │ consume│  (Worker) │
│           │        │             │        │           │
└───────────┘        └─────────────┘        └───────────┘
     │                     │                     │
 Sends messages     Stores messages       Processes messages
 (async, fire      (durable, ordered)    (one at a time,
  and forget)                             acknowledges)
```

### 3.2 Key Terms

| Term | Definition |
|------|------------|
| **Producer** | Application that sends messages |
| **Consumer** | Application that receives and processes messages |
| **Queue** | Buffer that stores messages |
| **Broker** | Server that manages queues (RabbitMQ, Kafka, etc.) |
| **Message** | Data being sent (usually JSON) |
| **Exchange** | Routes messages to queues (RabbitMQ concept) |
| **Acknowledge** | Consumer confirms message processed |

### 3.3 Delivery Guarantees

| Guarantee | Meaning | Trade-off |
|-----------|---------|-----------|
| **At-most-once** | Message delivered 0 or 1 times | Fast, may lose messages |
| **At-least-once** | Message delivered 1 or more times | Safe, may duplicate |
| **Exactly-once** | Message delivered exactly 1 time | Complex, slowest |

Most systems use **at-least-once** with **idempotent consumers**.

---

## 4. RabbitMQ Deep Dive

### 4.1 What is RabbitMQ?

RabbitMQ is an open-source message broker that implements AMQP (Advanced Message Queuing Protocol). It's known for:
- Reliability and durability
- Flexible routing via exchanges
- Mature ecosystem
- Easy to operate

### 4.2 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    RABBITMQ ARCHITECTURE                         │
└─────────────────────────────────────────────────────────────────┘

┌───────────┐     ┌──────────────────────────────────────────────┐
│           │     │               RABBITMQ BROKER                 │
│  Producer │     │                                              │
│           │─────│──▶┌──────────┐    ┌─────────┐   ┌─────────┐ │
└───────────┘     │   │ Exchange │───▶│ Queue 1 │──▶│Consumer1│ │
                  │   │          │    └─────────┘   └─────────┘ │
                  │   │ (routes) │                              │
                  │   │          │    ┌─────────┐   ┌─────────┐ │
                  │   │          │───▶│ Queue 2 │──▶│Consumer2│ │
                  │   └──────────┘    └─────────┘   └─────────┘ │
                  │                                              │
                  └──────────────────────────────────────────────┘
```

### 4.3 Exchange Types

| Type | Routing Behavior | Use Case |
|------|------------------|----------|
| **Direct** | Exact routing key match | Point-to-point |
| **Fanout** | Broadcast to all queues | Pub/sub |
| **Topic** | Pattern matching (*.error, order.#) | Selective routing |
| **Headers** | Match on message headers | Complex routing |

### 4.4 Direct Exchange

```
┌─────────────────────────────────────────────────────────────────┐
│                      DIRECT EXCHANGE                             │
└─────────────────────────────────────────────────────────────────┘

Producer sends with routing_key = "order.placed"

                    ┌──────────────────────────────────────────┐
                    │           Direct Exchange                 │
                    │                                          │
  ─── "order.placed"──▶│  Routing Key    →    Queue           │
                    │  ────────────────────────────           │
                    │  "order.placed"  →  order_notifications  │◀─ Match!
                    │  "order.shipped" →  shipping_queue       │
                    │                                          │
                    └──────────────────────────────────────────┘
```

### 4.5 Topic Exchange

```
┌─────────────────────────────────────────────────────────────────┐
│                      TOPIC EXCHANGE                              │
└─────────────────────────────────────────────────────────────────┘

Routing patterns:
  * = exactly one word
  # = zero or more words

Example bindings:
  "order.*"     → order_events      (matches order.placed, order.cancelled)
  "*.error"     → error_queue       (matches order.error, payment.error)
  "notification.#" → all_notifications  (matches notification.email.sent)
```

### 4.6 Work Queue Pattern

For distributing tasks across multiple workers:

```
┌─────────────────────────────────────────────────────────────────┐
│                      WORK QUEUE                                  │
└─────────────────────────────────────────────────────────────────┘

Producer                          Consumers (workers)

   │                              ┌──────────┐
   │    ┌─────────────────────┐   │ Worker 1 │
   │    │                     │──▶│ (busy)   │
   ├───▶│  notification_queue │   └──────────┘
   │    │                     │   ┌──────────┐
   │    │  [M1][M2][M3][M4]   │──▶│ Worker 2 │
   │    │                     │   │ (ready)  │
   │    └─────────────────────┘   └──────────┘

   Messages distributed round-robin
   Each message processed by ONE worker
   Workers acknowledge when done
```

### 4.7 Message Durability

To survive broker restarts:

```javascript
// 1. Declare durable queue
channel.assertQueue('notifications', { durable: true });

// 2. Publish persistent messages
channel.sendToQueue('notifications', Buffer.from(JSON.stringify(message)), {
  persistent: true
});

// 3. Manual acknowledgment (after processing)
channel.consume('notifications', (msg) => {
  processNotification(msg);
  channel.ack(msg);  // Only after successful processing
}, { noAck: false });
```

### 4.8 Acknowledgments

```
┌─────────────────────────────────────────────────────────────────┐
│                    MESSAGE ACKNOWLEDGMENT                        │
└─────────────────────────────────────────────────────────────────┘

POSITIVE ACK (message processed successfully):

  Consumer ─────▶ Process ─────▶ ack(msg) ─────▶ Message removed
                                                 from queue


NEGATIVE ACK (message processing failed):

  Consumer ─────▶ Process ─────▶ nack(msg, requeue=true) ─────▶ Back to queue
                   FAILS                                        (retry)


NEGATIVE ACK (don't retry):

  Consumer ─────▶ Process ─────▶ nack(msg, requeue=false) ─────▶ Dead letter
                   FAILS                                         queue
```

### 4.9 Dead Letter Queues

Messages that can't be processed go to a special queue:

```
┌─────────────────────────────────────────────────────────────────┐
│                   DEAD LETTER QUEUE                              │
└─────────────────────────────────────────────────────────────────┘

Main Queue                                      Dead Letter Queue
    │                                                  │
    │  Message fails 3 times                          │
    │  ─────────────────────────────────────────────▶ │
    │  (or exceeds TTL)                               │
    │                                                  │
    │                                            ┌────┴────┐
    │                                            │ Inspect │
    │                                            │  Debug  │
    │                                            │ Retry?  │
    │                                            └─────────┘
```

Configuration:
```javascript
// Main queue with dead letter config
channel.assertQueue('notifications', {
  durable: true,
  arguments: {
    'x-dead-letter-exchange': '',
    'x-dead-letter-routing-key': 'notifications.dead'
  }
});

// Dead letter queue
channel.assertQueue('notifications.dead', { durable: true });
```

---

## 5. Integration Patterns

### 5.1 Fire and Forget

Simplest pattern. Producer sends message and continues.

```javascript
// Producer (in order service)
async function placeOrder(order) {
  await orderRepository.save(order);

  // Fire and forget - don't wait for result
  messageQueue.publish('notifications', {
    type: 'ORDER_PLACED',
    orderId: order.id,
    customerId: order.customerId
  });

  return order; // Return immediately
}
```

### 5.2 Request-Reply (RPC)

For when you need a response (less common with queues):

```
┌─────────────────────────────────────────────────────────────────┐
│                    REQUEST-REPLY PATTERN                         │
└─────────────────────────────────────────────────────────────────┘

┌───────────┐                                    ┌───────────┐
│  Client   │                                    │  Server   │
└─────┬─────┘                                    └─────┬─────┘
      │                                                │
      │  1. Send request (with reply queue)            │
      │ ──────────────────────────────────────────────▶│
      │                                                │
      │                                   2. Process   │
      │                                                │
      │  3. Send reply to reply queue                  │
      │◀──────────────────────────────────────────────│
      │                                                │
```

### 5.3 Pub/Sub (Fanout)

Multiple consumers receive the same message:

```
┌─────────────────────────────────────────────────────────────────┐
│                    PUBLISH-SUBSCRIBE                             │
└─────────────────────────────────────────────────────────────────┘

     ORDER_PLACED event

           │
           ▼
      ┌─────────────┐
      │   Fanout    │
      │  Exchange   │
      └──────┬──────┘
             │
     ┌───────┼───────┐
     │       │       │
     ▼       ▼       ▼
┌───────┐┌───────┐┌───────┐
│ Email ││ Push  ││ Audit │
│ Queue ││ Queue ││ Queue │
└───────┘└───────┘└───────┘
     │       │       │
     ▼       ▼       ▼
  Email   Push    Log to
  Worker  Worker  Analytics
```

---

## 6. Error Handling and Reliability

### 6.1 Retry Strategies

**Immediate Retry:**
```javascript
async function processWithRetry(message, maxRetries = 3) {
  let attempt = 0;
  while (attempt < maxRetries) {
    try {
      await processMessage(message);
      return; // Success
    } catch (error) {
      attempt++;
      if (attempt >= maxRetries) throw error;
      // Immediate retry
    }
  }
}
```

**Exponential Backoff:**
```javascript
async function processWithBackoff(message, maxRetries = 3) {
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      await processMessage(message);
      return;
    } catch (error) {
      if (attempt >= maxRetries - 1) throw error;

      const delay = Math.pow(2, attempt) * 1000; // 1s, 2s, 4s...
      await sleep(delay);
    }
  }
}
```

**Delayed Retry Queue:**
```
┌─────────────────────────────────────────────────────────────────┐
│                   DELAYED RETRY                                  │
└─────────────────────────────────────────────────────────────────┘

Main Queue ────▶ Process ────▶ FAIL ────▶ Retry Queue (TTL: 5min)
                                                │
                                           (after 5 min)
                                                │
                                                ▼
                              ────▶ Back to Main Queue
```

### 6.2 Idempotency

Since at-least-once delivery may duplicate messages, consumers must be idempotent:

```javascript
// BAD: Not idempotent
async function sendNotification(message) {
  await pushService.send(message.customerId, message.text);
  // If this runs twice, customer gets two notifications!
}

// GOOD: Idempotent
async function sendNotification(message) {
  const alreadySent = await notificationLog.exists(message.id);
  if (alreadySent) {
    return; // Skip duplicate
  }

  await pushService.send(message.customerId, message.text);
  await notificationLog.record(message.id);
}
```

### 6.3 Circuit Breaker

Prevent cascading failures when external service is down:

```javascript
const CircuitBreaker = require('opossum');

const pushServiceBreaker = new CircuitBreaker(pushService.send, {
  timeout: 3000,          // 3 second timeout
  errorThresholdPercentage: 50,  // Open after 50% failures
  resetTimeout: 30000     // Try again after 30 seconds
});

async function sendNotification(message) {
  try {
    await pushServiceBreaker.fire(message.customerId, message.text);
  } catch (error) {
    if (error.message === 'Circuit breaker is open') {
      // Push service is down, queue for later
      await retryQueue.add(message, { delay: 60000 });
    }
  }
}
```

---

## 7. Monitoring and Operations

### 7.1 Key Metrics

| Metric | What It Tells You |
|--------|-------------------|
| **Queue depth** | Messages waiting to be processed |
| **Publish rate** | Messages being added per second |
| **Consume rate** | Messages being processed per second |
| **Ack rate** | Successful processing rate |
| **Unacked messages** | Messages being processed right now |
| **Consumer count** | Number of workers |
| **Redelivery rate** | How often messages are retried |
| **Dead letter count** | Failed messages |

### 7.2 RabbitMQ Management UI

RabbitMQ includes a web-based management console:

```
http://localhost:15672

Features:
- Queue overview (depth, rates)
- Connection and channel monitoring
- Exchange and binding management
- Message inspection
- User management
```

### 7.3 Alerting Thresholds

| Condition | Alert Level | Action |
|-----------|-------------|--------|
| Queue depth > 1000 | Warning | Check consumer health |
| Queue depth > 10000 | Critical | Scale consumers |
| Dead letters > 100 | Warning | Investigate failures |
| No consumers | Critical | Workers crashed |
| Redelivery > 20% | Warning | Bug in consumer |

### 7.4 Operational Tasks

| Task | Frequency |
|------|-----------|
| Monitor queue depth | Continuous |
| Review dead letters | Daily |
| Rotate logs | Weekly |
| Backup configuration | Before changes |
| Update RabbitMQ | Quarterly |

---

## 8. Alternative Technologies

### 8.1 Comparison

| Feature | RabbitMQ | Redis Queue | Kafka | SQS |
|---------|----------|-------------|-------|-----|
| **Type** | Message broker | In-memory | Event streaming | Cloud queue |
| **Persistence** | Yes | Optional | Yes | Yes |
| **Ordering** | Per-queue | FIFO | Per-partition | FIFO optional |
| **Throughput** | High | Very high | Very high | High |
| **Complexity** | Medium | Low | High | Low |
| **Use case** | Task queues, routing | Simple queues, cache | Event streaming, logs | Managed queues |
| **Self-hosted** | Yes | Yes | Yes | No (AWS only) |
| **Cost** | Server + ops | Server + ops | Server + ops | Pay per use |

### 8.2 When to Use Each

| Technology | Best For |
|------------|----------|
| **RabbitMQ** | Complex routing, reliable task queues, enterprise features |
| **Redis Queue** | Simple queues, already using Redis, low latency |
| **Kafka** | Event sourcing, high throughput, replay capability |
| **SQS** | AWS ecosystem, managed, serverless |
| **Google Pub/Sub** | GCP ecosystem, managed, global |

### 8.3 Why RabbitMQ for Q2

| Reason | Explanation |
|--------|-------------|
| **Feature completeness** | Dead letters, TTL, priority, all built-in |
| **Reliability** | Battle-tested, durable by default |
| **Routing flexibility** | Exchanges allow complex routing later |
| **Management UI** | Easy to monitor and debug |
| **Moderate scale** | Perfect for Q2 volume (not overkill) |
| **Future flexibility** | Can add more use cases (cache invalidation, events) |

---

## 9. Application to Q2

### 9.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                Q2 ASYNC NOTIFICATION ARCHITECTURE                │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐                              ┌─────────────────────┐
│   Order     │  1. Order placed             │                     │
│   Service   │──────────────────────────────│  Response (fast)    │
│             │                              │                     │
│  2. Publish │                              └─────────────────────┘
│     to queue│
└──────┬──────┘
       │
       │  "ORDER_PLACED" event
       ▼
┌─────────────────────────────────────────────────────────────────┐
│                        RABBITMQ                                  │
│                                                                 │
│  ┌────────────────┐                                             │
│  │ notifications  │ ◀── Direct exchange                        │
│  │    exchange    │                                             │
│  └───────┬────────┘                                             │
│          │                                                      │
│  ┌───────┴───────────────────────────────────────────┐          │
│  │                                                   │          │
│  ▼                                                   ▼          │
│ ┌───────────────────┐                    ┌───────────────────┐  │
│ │ customer_notifs   │                    │ restaurant_notifs │  │
│ │ [M1][M2][M3]      │                    │ [M1][M2]          │  │
│ └─────────┬─────────┘                    └─────────┬─────────┘  │
│           │                                        │            │
└───────────│────────────────────────────────────────│────────────┘
            │                                        │
            ▼                                        ▼
    ┌───────────────┐                        ┌───────────────┐
    │  Customer     │                        │  Restaurant   │
    │  Notification │                        │  Notification │
    │  Worker       │                        │  Worker       │
    │               │                        │               │
    │ - Push notifs │                        │ - Dashboard   │
    │ - Email?      │                        │   alerts      │
    └───────────────┘                        └───────────────┘
            │                                        │
            ▼                                        ▼
    ┌───────────────┐                        ┌───────────────┐
    │  Firebase     │                        │  Firebase /   │
    │  Push Service │                        │  WebSocket    │
    └───────────────┘                        └───────────────┘
```

### 9.2 Message Types

| Event | Producer | Consumer | Data |
|-------|----------|----------|------|
| `ORDER_PLACED` | Order Service | Notification Worker | orderId, customerId, restaurantId |
| `ORDER_ACCEPTED` | Order Service | Notification Worker | orderId, customerId, estimatedTime |
| `ORDER_REJECTED` | Order Service | Notification Worker | orderId, customerId, reason |
| `DRIVER_ASSIGNED` | Delivery Service | Notification Worker | orderId, customerId, driverId |
| `ORDER_DELIVERED` | Delivery Service | Notification Worker | orderId, customerId |

### 9.3 Implementation Code

**Publisher (Order Service):**
```javascript
// notification-publisher.js
const amqp = require('amqplib');

let channel;

async function connect() {
  const connection = await amqp.connect(process.env.RABBITMQ_URL);
  channel = await connection.createChannel();

  // Ensure exchange exists
  await channel.assertExchange('notifications', 'direct', { durable: true });
}

async function publishNotification(event) {
  const routingKey = event.type.toLowerCase(); // e.g., 'order_placed'

  channel.publish(
    'notifications',
    routingKey,
    Buffer.from(JSON.stringify(event)),
    { persistent: true }
  );
}

// Usage in order service
async function placeOrder(order) {
  const savedOrder = await orderRepository.save(order);

  // Fire and forget
  await publishNotification({
    type: 'ORDER_PLACED',
    orderId: savedOrder.id,
    customerId: savedOrder.customerId,
    restaurantId: savedOrder.restaurantId,
    timestamp: new Date().toISOString()
  });

  return savedOrder;
}
```

**Consumer (Notification Worker):**
```javascript
// notification-worker.js
const amqp = require('amqplib');
const pushService = require('./push-service');

async function startWorker() {
  const connection = await amqp.connect(process.env.RABBITMQ_URL);
  const channel = await connection.createChannel();

  // Ensure queue exists with dead letter config
  await channel.assertQueue('customer_notifications', {
    durable: true,
    arguments: {
      'x-dead-letter-exchange': '',
      'x-dead-letter-routing-key': 'customer_notifications.dead'
    }
  });

  // Bind to exchange
  await channel.bindQueue('customer_notifications', 'notifications', 'order_placed');
  await channel.bindQueue('customer_notifications', 'notifications', 'order_accepted');

  // Process one at a time
  channel.prefetch(1);

  channel.consume('customer_notifications', async (msg) => {
    const event = JSON.parse(msg.content.toString());

    try {
      await processNotification(event);
      channel.ack(msg);
    } catch (error) {
      console.error('Failed to process notification:', error);

      // Retry up to 3 times, then dead letter
      const retryCount = (msg.properties.headers?.['x-retry-count'] || 0) + 1;
      if (retryCount < 3) {
        // Republish with incremented retry count
        channel.publish('', 'customer_notifications', msg.content, {
          persistent: true,
          headers: { 'x-retry-count': retryCount }
        });
        channel.ack(msg);
      } else {
        channel.nack(msg, false, false); // Send to dead letter
      }
    }
  }, { noAck: false });
}

async function processNotification(event) {
  switch (event.type) {
    case 'ORDER_PLACED':
      await pushService.sendToCustomer(event.customerId, {
        title: 'Order Received',
        body: 'Your order has been sent to the restaurant'
      });
      break;
    case 'ORDER_ACCEPTED':
      await pushService.sendToCustomer(event.customerId, {
        title: 'Order Accepted',
        body: `Estimated delivery: ${event.estimatedTime}`
      });
      break;
    // ... other cases
  }
}

startWorker();
```

### 9.4 Reusing for Cache Invalidation

Since we're adding RabbitMQ, we can also use it for cache invalidation:

```javascript
// When menu item is updated
await channel.publish('cache_invalidation', 'menu_updated', Buffer.from(JSON.stringify({
  restaurantId: item.restaurantId,
  itemId: item.id
})));

// Cache service listens and invalidates
channel.consume('cache_invalidation_queue', async (msg) => {
  const event = JSON.parse(msg.content.toString());
  await redis.del(`menu:${event.restaurantId}`);
  channel.ack(msg);
});
```

---

## 10. References

### Documentation

| Resource | Link |
|----------|------|
| RabbitMQ Docs | https://www.rabbitmq.com/documentation.html |
| RabbitMQ Tutorials | https://www.rabbitmq.com/getstarted.html |
| AMQP Protocol | https://www.amqp.org/ |

### Articles

| Article | Source |
|---------|--------|
| Message Queuing Patterns | https://www.enterpriseintegrationpatterns.com/ |
| RabbitMQ Best Practices | https://www.cloudamqp.com/blog/part1-rabbitmq-best-practice.html |

### Libraries

| Language | Library | Link |
|----------|---------|------|
| Node.js | amqplib | https://www.npmjs.com/package/amqplib |
| Python | pika | https://pypi.org/project/pika/ |
| Java | spring-rabbit | https://spring.io/projects/spring-amqp |
| .NET | RabbitMQ.Client | https://www.nuget.org/packages/RabbitMQ.Client |

### Tools

| Tool | Purpose |
|------|---------|
| RabbitMQ Management Plugin | Web UI for monitoring |
| CloudAMQP | Managed RabbitMQ hosting |
| LavinMQ | Lightweight alternative |

---

## Document Summary

For Q2's notification slowdown problem:

1. **Deploy RabbitMQ** as message broker
2. **Create notification exchange** with direct routing
3. **Implement notification workers** that process messages asynchronously
4. **Add retry logic** with exponential backoff
5. **Configure dead letter queues** for failed messages
6. **Monitor queue depth** and worker health
7. **Reuse for cache invalidation** (bonus)

This decouples notifications from the request path, providing immediate response to users while ensuring reliable delivery of notifications in the background.
