# Food Delivery Platform - System Flowchart

This document contains the complete system flowchart showing the full order lifecycle with all actors involved, decision points, and state transitions.

---

## 1. Complete Order Flow - All Actors Combined

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           COMPLETE ORDER FLOW                                    │
│                     (Customer + Restaurant + Driver)                             │
└─────────────────────────────────────────────────────────────────────────────────┘

    CUSTOMER                          SYSTEM                         RESTAURANT
    ────────                          ──────                         ──────────
        │                                │                                │
        ▼                                │                                │
┌───────────────┐                        │                                │
│ Browse Menu   │                        │                                │
│ Add to Basket │                        │                                │
│ Checkout      │                        │                                │
└───────┬───────┘                        │                                │
        │                                │                                │
        ▼                                │                                │
┌───────────────┐                        │                                │
│ Submit Order  │────────────────────────▶                                │
└───────────────┘                        │                                │
        │                                ▼                                │
        │                    ┌───────────────────────┐                    │
        │                    │   VALIDATE ORDER      │                    │
        │                    │   - Check items exist │                    │
        │                    │   - Check available   │                    │
        │                    │   - Calculate total   │                    │
        │                    │   - Add platform fee  │                    │
        │                    └───────────┬───────────┘                    │
        │                                │                                │
        │                                │ Valid                          │
        │                                ▼                                │
        │                    ┌───────────────────────┐                    │
        │                    │  CREATE ORDER         │                    │
        │                    │  State: PENDING_      │                    │
        │                    │         ACCEPTANCE    │                    │
        │                    └───────────┬───────────┘                    │
        │                                │                                │
        │                                │ Send notification              │
        │                                ▼                                │
        │                    ┌───────────────────────┐         ┌─────────────────┐
        │                    │   PUSH NOTIFICATION   │────────▶│  Incoming Queue │
        │                    │   to Restaurant       │         │  + Alert        │
        │                    └───────────────────────┘         └────────┬────────┘
        │                                                               │
        │                                                               ▼
        │                                                      ┌───────────────┐
        │                                                      │ Review Order  │
        │                                                      └───────┬───────┘
        │                                                               │
        │                              ┌────────────────────────────────┼────────┐
        │                              │                                │        │
        │                              ▼                                ▼        │
        │                    ┌─────────────────┐              ┌─────────────────┐│
        │                    │     REJECT?     │              │     ACCEPT?     ││
        │                    └────────┬────────┘              └────────┬────────┘│
        │                             │                                │         │
        │                             ▼                                │         │
        │                    ┌─────────────────┐                       │         │
        │                    │ UPDATE ORDER    │                       │         │
        │                    │ State: REJECTED │                       │         │
        │                    └────────┬────────┘                       │         │
        │                             │                                │         │
        │◀────────────────────────────┤                                │         │
        ▼                             │                                │         │
┌───────────────┐                     │                                │         │
│ "Order could  │                     │                                │         │
│ not be        │                     │                                │         │
│ fulfilled"    │                     │                                │         │
└───────────────┘                     │                                │         │
        │                             │                                │         │
        ▼                             │                                │         │
      [END]                           │                                │         │
                                      │                                │         │
        ┌─────────────────────────────┘                                │         │
        │                                                              │         │
        │                                                              ▼         │
        │                                                    ┌─────────────────┐ │
        │                                                    │ UPDATE ORDER    │ │
        │                                                    │ State: ACCEPTED │ │
        │                                                    └────────┬────────┘ │
        │                                                             │          │
        │                                ┌────────────────────────────┘          │
        │                                │                                       │
        │                                ▼                                       │
        │                    ┌───────────────────────┐                           │
        │                    │   PROCESS PAYMENT     │                           │
        │                    │   (via Payment        │                           │
        │                    │    Provider)          │                           │
        │                    └───────────┬───────────┘                           │
        │                                │                                       │
        │               ┌────────────────┼────────────────┐                      │
        │               │                │                │                      │
        │               ▼                │                ▼                      │
        │    ┌─────────────────┐         │     ┌─────────────────┐               │
        │    │  PAYMENT FAILS  │         │     │PAYMENT SUCCEEDS │               │
        │    └────────┬────────┘         │     └────────┬────────┘               │
        │             │                  │              │                        │
        │             ▼                  │              ▼                        │
        │    ┌─────────────────┐         │     ┌─────────────────┐               │
        │    │ UPDATE ORDER    │         │     │ UPDATE ORDER    │               │
        │    │ State: PAYMENT_ │         │     │ State: PREPARING│               │
        │    │        FAILED   │         │     └────────┬────────┘               │
        │    └────────┬────────┘         │              │                        │
        │             │                  │              │                        │
        │◀────────────┤                  │              │ Notify restaurant      │
        ▼             │                  │              ▼                        │
┌───────────────┐     │                  │     ┌─────────────────┐      ┌────────┴────────┐
│ "Payment      │     │                  │     │ CREATE PAYMENT  │      │ Preparation     │
│ failed,       │     │                  │     │ RECORD          │      │ Queue + Alert   │
│ please retry" │     │                  │     └─────────────────┘      └────────┬────────┘
└───────────────┘     │                  │                                       │
        │             │                  │                                       ▼
        ▼             │                  │                              ┌───────────────┐
   [RETRY]            │                  │                              │ Prepare Food  │
                      │                  │                              └───────┬───────┘
                      │                  │                                      │
                      │                  │                                      ▼
                      │                  │                              ┌───────────────┐
                      │                  │                              │ Mark "Ready"  │
                      │                  │                              └───────┬───────┘
                      │                  │                                      │
                      │                  │                                      ▼
                      │                  │                              ┌───────────────┐
                      │                  │                              │ Assign Driver │
                      │                  │                              │ (from signed- │
                      │                  │                              │  in drivers)  │
                      │                  │                              └───────┬───────┘
                      │                  │                                      │
                      │                  │                                      │
                      └──────────────────┼──────────────────────────────────────┘
                                         │
                                         │
    CUSTOMER                          SYSTEM                           DRIVER
    ────────                          ──────                           ──────
        │                                │                                │
        │                                │ Notify driver                  │
        │                                ▼                                │
        │                    ┌───────────────────────┐         ┌─────────────────┐
        │                    │ PUSH NOTIFICATION     │────────▶│ View Delivery   │
        │                    │ to Driver             │         │ Assignment      │
        │                    └───────────────────────┘         └────────┬────────┘
        │                                                               │
        │                                                               ▼
        │                                                      ┌───────────────┐
        │                                                      │ Acknowledge   │
        │                                                      │ Assignment    │
        │                                                      └───────┬───────┘
        │                                                               │
        │                                                               ▼
        │                                                      ┌───────────────┐
        │                                                      │ Pick Up Food  │
        │                                                      │ from          │
        │                                                      │ Restaurant    │
        │                                                      └───────┬───────┘
        │                                                               │
        │                                                               ▼
        │                                                      ┌───────────────┐
        │                                                      │ Mark Status:  │
        │                                                      │ "In Route"    │
        │                    ┌───────────────────────┐         └───────┬───────┘
        │                    │ UPDATE ORDER          │◀────────────────┘
        │                    │ State: OUT_FOR_       │
        │                    │        DELIVERY       │
        │                    └───────────┬───────────┘
        │◀───────────────────────────────┤
        ▼                                │
┌───────────────┐                        │
│ Progress:     │                        │
│ ● Accepted    │                        │
│ ● Preparing   │                        │
│ ● Out for     │                        │
│   Delivery    │                        │
│ ○ Delivered   │                        │
└───────────────┘                        │
        │                                │
        │                                │                                │
        │                                │                                ▼
        │                                │                       ┌───────────────┐
        │                                │                       │ Deliver to    │
        │                                │                       │ Customer      │
        │                                │                       │ (call/knock)  │
        │                                │                       └───────┬───────┘
        │                                │                                │
        │                                │                                ▼
        │                                │                       ┌───────────────┐
        │                                │                       │ Mark Status:  │
        │                                │                       │ "Delivered"   │
        │                    ┌───────────────────────┐           └───────┬───────┘
        │                    │ UPDATE ORDER          │◀──────────────────┘
        │                    │ State: DELIVERED      │
        │                    └───────────┬───────────┘
        │◀───────────────────────────────┤
        ▼                                │
┌───────────────┐                        │
│ Progress:     │                        │
│ ● Accepted    │                        │
│ ● Preparing   │                        │
│ ● Out for     │                        │
│   Delivery    │                        │
│ ● Delivered   │                        │
└───────────────┘                        │
        │                                │
        ▼                                ▼
┌───────────────┐            ┌───────────────────────┐
│ Order         │            │ UPDATE ORDER          │
│ Complete      │            │ State: COMPLETED      │
│               │            │                       │
│ (View in      │            │ Record in history     │
│  history)     │            │ for all actors        │
└───────────────┘            └───────────────────────┘
```

---

## 2. Order State Machine

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ORDER STATE MACHINE                          │
└─────────────────────────────────────────────────────────────────────┘

                    ┌─────────────────────┐
                    │  PENDING_ACCEPTANCE │ ◀── Order created
                    └──────────┬──────────┘
                               │
              ┌────────────────┼────────────────┐
              │                │                │
              ▼                │                ▼
    ┌─────────────────┐        │      ┌─────────────────┐
    │    REJECTED     │        │      │    ACCEPTED     │
    └─────────────────┘        │      └────────┬────────┘
              │                │               │
              ▼                │               ▼
           [END]               │      ┌─────────────────┐
                               │      │ PAYMENT PENDING │ (implicit)
                               │      └────────┬────────┘
                               │               │
                               │    ┌──────────┼──────────┐
                               │    │          │          │
                               │    ▼          │          ▼
                               │ ┌──────────┐  │  ┌─────────────────┐
                               │ │ PAYMENT_ │  │  │   PREPARING     │
                               │ │ FAILED   │  │  └────────┬────────┘
                               │ └──────────┘  │           │
                               │      │        │           ▼
                               │      ▼        │  ┌─────────────────┐
                               │   [END]       │  │     READY       │
                               │               │  └────────┬────────┘
                               │               │           │
                               │               │           ▼
                               │               │  ┌─────────────────┐
                               │               │  │ OUT_FOR_DELIVERY│
                               │               │  └────────┬────────┘
                               │               │           │
                               │               │           ▼
                               │               │  ┌─────────────────┐
                               │               │  │   DELIVERED     │
                               │               │  └────────┬────────┘
                               │               │           │
                               │               │           ▼
                               │               │  ┌─────────────────┐
                               │               │  │   COMPLETED     │
                               │               │  └─────────────────┘
                               │               │
                               └───────────────┘
```

### Order States Table

| State | Description | Triggered By | Next States |
|-------|-------------|--------------|-------------|
| PENDING_ACCEPTANCE | Order submitted, waiting for restaurant | System (on order creation) | ACCEPTED, REJECTED |
| REJECTED | Restaurant cannot fulfill order | Restaurant | (terminal) |
| ACCEPTED | Restaurant accepted, payment pending | Restaurant | PREPARING, PAYMENT_FAILED |
| PAYMENT_FAILED | Payment could not be processed | Payment Provider | (terminal) |
| PREPARING | Food being prepared | System (on payment success) | READY |
| READY | Food ready for pickup | Restaurant | OUT_FOR_DELIVERY |
| OUT_FOR_DELIVERY | Driver en route to customer | Driver | DELIVERED |
| DELIVERED | Driver marked as delivered | Driver | COMPLETED |
| COMPLETED | Order finished | System | (terminal) |

---

## 3. Decision Points Summary

| Decision Point | Actor | Options | Outcome |
|----------------|-------|---------|---------|
| Accept/Reject Order | Restaurant | Accept / Reject | Accept → Payment processing; Reject → Order cancelled |
| Payment Result | System (Payment Provider) | Success / Fail | Success → Preparing; Fail → Payment failed |
| Driver Assignment | Restaurant | Select driver | Driver notified of delivery |
| Delivery Completion | Driver | Mark delivered | Order completed |

---

## 4. Notification Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                        NOTIFICATION FLOW                            │
└─────────────────────────────────────────────────────────────────────┘

EVENT                              RECIPIENT              NOTIFICATION
─────                              ─────────              ────────────

Order Placed                  ───▶ Restaurant        ───▶ "New order received"
                                                          (Push + Dashboard update)

Order Rejected               ───▶ Customer          ───▶ "Order could not be fulfilled"

Payment Failed               ───▶ Customer          ───▶ "Payment failed, please retry"

Payment Succeeded            ───▶ Restaurant        ───▶ "Payment confirmed, start preparing"
                             ───▶ Customer          ───▶ "Order confirmed"

Driver Assigned              ───▶ Driver            ───▶ "New delivery assigned"
                                                          (Push + App update)

Status: In Route             ───▶ Customer          ───▶ Progress bar update

Status: Delivered            ───▶ Customer          ───▶ "Your order has been delivered"
                             ───▶ Restaurant        ───▶ Order marked complete
```

---

## 5. Queue Management (Restaurant Dashboard)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    RESTAURANT QUEUE MANAGEMENT                      │
└─────────────────────────────────────────────────────────────────────┘

                    ┌─────────────────────────────────┐
                    │      INCOMING QUEUE             │
                    │   (Orders awaiting decision)    │
                    │                                 │
                    │   ┌─────────────────────────┐   │
                    │   │ Order #001 - 6:01 PM    │◀──│── Oldest (first out)
                    │   │ 2x Burger, 1x Fries     │   │
                    │   │ [ACCEPT] [REJECT]       │   │
                    │   └─────────────────────────┘   │
                    │   ┌─────────────────────────┐   │
                    │   │ Order #002 - 6:03 PM    │   │
                    │   │ 1x Pizza                │   │
                    │   │ [ACCEPT] [REJECT]       │   │
                    │   └─────────────────────────┘   │
                    │   ┌─────────────────────────┐   │
                    │   │ Order #003 - 6:05 PM    │◀──│── Newest (last out)
                    │   │ 3x Salad                │   │
                    │   │ [ACCEPT] [REJECT]       │   │
                    │   └─────────────────────────┘   │
                    │                                 │
                    │         FIFO ORDER              │
                    └─────────────────────────────────┘
                                   │
                                   │ On Accept (after payment succeeds)
                                   ▼
                    ┌─────────────────────────────────┐
                    │     PREPARATION QUEUE           │
                    │   (Orders being prepared)       │
                    │                                 │
                    │   ┌─────────────────────────┐   │
                    │   │ Order #001 - Preparing  │   │
                    │   │ [MARK READY]            │   │
                    │   └─────────────────────────┘   │
                    │   ┌─────────────────────────┐   │
                    │   │ Order #002 - Ready      │   │
                    │   │ [ASSIGN DRIVER ▼]       │   │
                    │   └─────────────────────────┘   │
                    │                                 │
                    └─────────────────────────────────┘
```

---

## 6. Delivery State Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                      DELIVERY STATE FLOW                            │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  ASSIGNED   │────▶│ ACKNOWLEDGED│────▶│  PICKED_UP  │────▶│  IN_ROUTE   │────▶│  DELIVERED  │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
      │                   │                   │                   │                   │
      │                   │                   │                   │                   │
      ▼                   ▼                   ▼                   ▼                   ▼
 Restaurant          Driver              Driver              Driver              Driver
 assigns             confirms            collects            marks               marks
 driver              they saw it         food                "In Route"          "Delivered"
```

### Delivery States Table

| State | Description | Triggered By | Customer Sees |
|-------|-------------|--------------|---------------|
| ASSIGNED | Restaurant assigned driver to order | Restaurant | (no change) |
| ACKNOWLEDGED | Driver confirmed they see assignment | Driver | (no change) |
| PICKED_UP | Driver collected food from restaurant | Driver | (no change) |
| IN_ROUTE | Driver heading to customer | Driver | "Out for Delivery" ● |
| DELIVERED | Driver completed delivery | Driver | "Delivered" ● |

---

## 7. Payment Flow Detail

```
┌─────────────────────────────────────────────────────────────────────┐
│                        PAYMENT FLOW                                 │
└─────────────────────────────────────────────────────────────────────┘

                    ┌─────────────────────┐
                    │  Restaurant Accepts │
                    │       Order         │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │  Payment Module     │
                    │  receives request   │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │  Create Payment     │
                    │  Record             │
                    │  State: PENDING     │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │  Call Payment       │──────────────────▶ External
                    │  Provider           │                    Payment
                    │  (Stripe, etc.)     │◀────────────────── Provider
                    │  State: PROCESSING  │
                    └──────────┬──────────┘
                               │
              ┌────────────────┼────────────────┐
              │                │                │
              ▼                │                ▼
    ┌─────────────────┐        │      ┌─────────────────┐
    │    FAILURE      │        │      │    SUCCESS      │
    │                 │        │      │                 │
    │ State: FAILED   │        │      │ State: SUCCEEDED│
    └────────┬────────┘        │      └────────┬────────┘
             │                 │               │
             ▼                 │               ▼
    ┌─────────────────┐        │      ┌─────────────────┐
    │ Order State:    │        │      │ Order State:    │
    │ PAYMENT_FAILED  │        │      │ PREPARING       │
    └────────┬────────┘        │      └────────┬────────┘
             │                 │               │
             ▼                 │               ▼
    ┌─────────────────┐        │      ┌─────────────────┐
    │ Notify Customer │        │      │ Notify Customer │
    │ "Payment failed"│        │      │ "Order confirmed│
    │                 │        │      │                 │
    │ Option: Retry   │        │      │ Notify          │
    └─────────────────┘        │      │ Restaurant      │
                               │      │ "Start preparing│
                               │      └─────────────────┘
                               │
                               └───────────────────────────
```

---

## 8. Order Total Calculation Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                    ORDER TOTAL CALCULATION                          │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│   Customer's Basket                                                │
│   ┌─────────────────────────────────────────────────────────────┐  │
│   │ Item              │ Price    │ Qty │ Subtotal               │  │
│   │───────────────────│──────────│─────│────────────────────────│  │
│   │ Burger            │ $8.00    │ 2   │ $16.00                 │  │
│   │ Fries             │ $3.50    │ 1   │ $3.50                  │  │
│   │ Drink             │ $2.00    │ 2   │ $4.00                  │  │
│   └─────────────────────────────────────────────────────────────┘  │
│                                                                     │
│   ┌─────────────────────────────────────────────────────────────┐  │
│   │ Items Total                                      $23.50     │  │
│   │ + Delivery Fee (set by restaurant)               $3.00      │  │
│   │ ─────────────────────────────────────────────────────────   │  │
│   │ Subtotal                                         $26.50     │  │
│   │ + Platform Fee (10% of subtotal)                 $2.65      │  │
│   │ ═════════════════════════════════════════════════════════   │  │
│   │ TOTAL (Customer Pays)                            $29.15     │  │
│   └─────────────────────────────────────────────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

FORMULA:
────────
items_total   = SUM(item_price × quantity)     ◀── Restaurant pricing
delivery_fee  = restaurant.delivery_fee        ◀── Restaurant sets
subtotal      = items_total + delivery_fee
platform_fee  = subtotal × PLATFORM_PERCENTAGE ◀── Platform sets (e.g., 10%)
total_amount  = subtotal + platform_fee        ◀── Customer pays this
```
