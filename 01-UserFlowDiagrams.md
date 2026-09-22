# Food Delivery Platform - User Flow Diagrams

This document contains the user flows for each actor in the system, showing what each user can do from the moment they enter the platform.

---

## 1. Customer Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                         CUSTOMER JOURNEY                            │
└─────────────────────────────────────────────────────────────────────┘

┌──────────────┐
│   START      │
└──────┬───────┘
       │
       ▼
┌──────────────┐     ┌──────────────┐
│ Has Account? │──No─▶│  Register    │
└──────┬───────┘     │  (username,  │
       │             │  password,   │
       │ Yes         │  address)    │
       │             └──────┬───────┘
       │                    │
       ▼◀───────────────────┘
┌──────────────┐
│    Login     │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│   Browse     │
│  Restaurant  │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│  View Menu   │◀─────────────────┐
│ (see items,  │                  │
│ availability)│                  │
└──────┬───────┘                  │
       │                          │
       ▼                          │
┌──────────────┐                  │
│ Add Items to │                  │
│   Basket     │──────────────────┘
└──────┬───────┘    (continue shopping)
       │
       │ (proceed to checkout)
       ▼
┌──────────────┐
│ View Basket  │
│ (items,      │
│ quantities,  │
│ totals)      │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│   Checkout   │
│ (confirm     │
│ address,     │
│ enter payment│
│ details)     │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Place Order  │
│ (submit)     │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                    WAITING STATE                          │
│                                                           │
│   Customer sees: "Waiting for restaurant to accept..."   │
│                                                           │
│   ┌─────────────┐                                        │
│   │ Restaurant  │──Reject──▶ "Order could not be        │
│   │  Decision   │            fulfilled" ──▶ END          │
│   └──────┬──────┘                                        │
│          │                                                │
│          │ Accept                                         │
│          ▼                                                │
│   ┌─────────────┐                                        │
│   │  Payment    │──Fails──▶ "Payment failed,            │
│   │  Processed  │           please retry" ──▶ Checkout   │
│   └──────┬──────┘                                        │
│          │                                                │
│          │ Success                                        │
│          ▼                                                │
└──────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                  ORDER TRACKING VIEW                      │
│                                                           │
│   Progress bar shows current status:                     │
│                                                           │
│   ● Order Accepted                                       │
│   ○ Preparing                                            │
│   ○ Out for Delivery                                     │
│   ○ Delivered                                            │
│                                                           │
│   (● = completed, ○ = pending)                           │
│                                                           │
│   Status updates as restaurant/driver progress           │
└──────────────────────────────────────────────────────────┘
       │
       │ (when driver marks delivered)
       ▼
┌──────────────┐
│    Order     │
│   Complete   │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ View Order   │
│   History    │
│ (past orders)│
└──────────────┘
```

### Customer Capabilities Summary

| Action | Description |
|--------|-------------|
| Register | Self-registration with username, password, address |
| Login | Authenticate to access the platform |
| Browse | View available restaurant(s) |
| View Menu | See menu items with prices and availability |
| Add to Basket | Select items and quantities |
| Checkout | Confirm address, enter payment details |
| Place Order | Submit order to restaurant |
| Track Order | See progress bar with status updates |
| View History | Access past orders |

---

## 2. Restaurant Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                        RESTAURANT JOURNEY                           │
└─────────────────────────────────────────────────────────────────────┘

┌──────────────┐
│   START      │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                    ONBOARDING                             │
│                                                           │
│   Platform creates restaurant account                    │
│   Restaurant receives: Restaurant ID + Password          │
└──────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────┐
│    Login     │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                    SETUP (One-time)                       │
│                                                           │
│   ┌─────────────┐                                        │
│   │ Setup Menu  │                                        │
│   │ - Add items │                                        │
│   │ - Set prices│                                        │
│   │ - Set       │                                        │
│   │   delivery  │                                        │
│   │   fee       │                                        │
│   └──────┬──────┘                                        │
│          │                                                │
│          ▼                                                │
│   ┌─────────────┐                                        │
│   │  Onboard    │                                        │
│   │  Drivers    │                                        │
│   │ - Create    │                                        │
│   │   driver    │                                        │
│   │   accounts  │                                        │
│   │ - Assign    │                                        │
│   │   Driver ID │                                        │
│   │ + password  │                                        │
│   └─────────────┘                                        │
└──────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                  DAILY OPERATIONS                         │
└──────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────┐
│  Dashboard   │◀─────────────────────────────────────────┐
│   View       │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ├──────────────────────────────────────┐           │
       │                                      │           │
       ▼                                      ▼           │
┌──────────────┐                    ┌──────────────┐      │
│   INCOMING   │                    │    MENU      │      │
│    QUEUE     │                    │  MANAGEMENT  │      │
│              │                    │              │      │
│ Orders       │                    │ - Edit items │      │
│ awaiting     │                    │ - Update     │      │
│ accept/reject│                    │   prices     │      │
│              │                    │ - Toggle     │      │
│ (FIFO order) │                    │   available/ │      │
└──────┬───────┘                    │   unavailable│      │
       │                            └──────────────┘      │
       ▼                                                   │
┌──────────────┐                                          │
│   Review     │                                          │
│    Order     │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ├────────Reject────▶ Order cancelled               │
       │                   Customer notified ─────────────┤
       │                                                   │
       │ Accept                                            │
       ▼                                                   │
┌──────────────┐                                          │
│  PREPARATION │                                          │
│    QUEUE     │                                          │
│              │                                          │
│ Accepted     │                                          │
│ orders being │                                          │
│ prepared     │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│   Prepare    │                                          │
│    Food      │                                          │
│              │                                          │
│ Update status│                                          │
│ to           │                                          │
│ "Preparing"  │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│  Mark Ready  │                                          │
│              │                                          │
│ Food is      │                                          │
│ prepared     │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐     ┌─────────────────────┐              │
│   Assign     │────▶│ Select from         │              │
│   Driver     │     │ available drivers   │              │
│              │     │ (signed in for      │              │
│              │     │ shift)              │              │
└──────┬───────┘     └─────────────────────┘              │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│   Monitor    │                                          │
│   Delivery   │                                          │
│              │                                          │
│ See driver   │                                          │
│ status       │                                          │
│ updates      │                                          │
└──────┬───────┘                                          │
       │                                                   │
       │ (when delivered)                                  │
       ▼                                                   │
┌──────────────┐                                          │
│   Order      │                                          │
│  Complete    │──────────────────────────────────────────┘
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                    REPORTING                              │
│                                                           │
│   - View order history                                   │
│   - See delivery completion rates                        │
│   - See completion times                                 │
└──────────────────────────────────────────────────────────┘
```

### Restaurant Capabilities Summary

| Action | Description |
|--------|-------------|
| Login | Authenticate with Restaurant ID + Password |
| Setup Menu | Add items, set prices, set delivery fee |
| Onboard Drivers | Create driver accounts with Driver ID + Password |
| View Incoming Queue | See orders awaiting acceptance (FIFO) |
| Accept/Reject Orders | Decide whether to fulfill order |
| Manage Menu | Edit items, update prices, toggle availability |
| Update Order Status | Mark as preparing, ready |
| Assign Driver | Select from signed-in drivers |
| Monitor Delivery | Track driver status updates |
| View Reports | Order history, completion rates, times |

---

## 3. Driver Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                          DRIVER JOURNEY                             │
└─────────────────────────────────────────────────────────────────────┘

┌──────────────┐
│   START      │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                    ONBOARDING                             │
│                                                           │
│   Restaurant creates driver account                      │
│   Driver receives: Driver ID + Password                  │
│   (Driver ID links driver to restaurant)                 │
└──────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────┐
│    Login     │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                   SHIFT MANAGEMENT                        │
└──────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────┐
│   Sign In    │
│  for Shift   │
│              │
│ (Marks self  │
│ as available │
│ for work)    │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│   AVAILABLE  │◀─────────────────────────────────────────┐
│    STATE     │                                          │
│              │                                          │
│ Waiting for  │                                          │
│ assignment   │                                          │
└──────┬───────┘                                          │
       │                                                   │
       │ (Restaurant assigns delivery)                     │
       ▼                                                   │
┌──────────────┐                                          │
│  Delivery    │                                          │
│  Assigned    │                                          │
│              │                                          │
│ - See order  │                                          │
│   details    │                                          │
│ - See pickup │                                          │
│   location   │                                          │
│ - See        │                                          │
│   delivery   │                                          │
│   address    │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│ Acknowledge  │                                          │
│  Assignment  │                                          │
│              │                                          │
│ (Confirms    │                                          │
│ driver has   │                                          │
│ seen it)     │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│   Pick Up    │                                          │
│    Food      │                                          │
│              │                                          │
│ Collect from │                                          │
│ restaurant   │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│ Mark Status: │                                          │
│  "In Route"  │                                          │
│              │                                          │
│ Customer sees│                                          │
│ update       │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│   Deliver    │                                          │
│   to         │                                          │
│  Customer    │                                          │
│              │                                          │
│ (Call/knock  │                                          │
│ on arrival)  │                                          │
└──────┬───────┘                                          │
       │                                                   │
       ▼                                                   │
┌──────────────┐                                          │
│ Mark Status: │                                          │
│ "Delivered"  │                                          │
│              │                                          │
│ Order        │                                          │
│ complete     │──────────────────────────────────────────┘
└──────┬───────┘     (return to available state)
       │
       │ (end of shift)
       ▼
┌──────────────┐
│  Sign Out    │
│  from Shift  │
│              │
│ (No longer   │
│ available)   │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                     HISTORY                               │
│                                                           │
│   - View past deliveries                                 │
│   - See delivery details                                 │
└──────────────────────────────────────────────────────────┘
```

### Driver Capabilities Summary

| Action | Description |
|--------|-------------|
| Login | Authenticate with Driver ID + Password |
| Sign In for Shift | Mark self as available for deliveries |
| View Assignment | See order details, pickup location, delivery address |
| Acknowledge | Confirm receipt of assignment |
| Pick Up Food | Collect order from restaurant |
| Mark In Route | Update status to show on the way |
| Deliver | Hand off to customer (call/knock on arrival) |
| Mark Delivered | Complete the delivery |
| Sign Out | End shift, become unavailable |
| View History | Access past deliveries |

---

## 4. Business/Platform Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                     BUSINESS/PLATFORM JOURNEY                       │
└─────────────────────────────────────────────────────────────────────┘

┌──────────────┐
│   START      │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Admin Login  │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────────┐
│                    ADMIN DASHBOARD                        │
└──────────────────────────────────────────────────────────┘
       │
       ├──────────────────────────────────────┐
       │                                      │
       ▼                                      ▼
┌──────────────┐                    ┌──────────────┐
│  ONBOARD     │                    │   GLOBAL     │
│ RESTAURANTS  │                    │  REPORTING   │
│              │                    │              │
│ - Create     │                    │ - Total      │
│   restaurant │                    │   orders     │
│   accounts   │                    │ - Revenue    │
│ - Assign     │                    │   (platform  │
│   Restaurant │                    │   fees)      │
│   ID +       │                    │ - Restaurants│
│   password   │                    │   onboarded  │
└──────────────┘                    │ - Basic      │
                                    │   averages   │
                                    └──────────────┘
```

### Business/Platform Capabilities Summary

| Action | Description |
|--------|-------------|
| Admin Login | Platform administrator access |
| Onboard Restaurants | Create restaurant accounts |
| View Global Reports | Total orders, revenue, restaurants, averages |

---

## Key Relationships Between Actors

```
┌─────────────────────────────────────────────────────────────────────┐
│                      ACTOR RELATIONSHIPS                            │
└─────────────────────────────────────────────────────────────────────┘

                         ┌──────────────┐
                         │   PLATFORM   │
                         │  (Business)  │
                         └──────┬───────┘
                                │
                                │ onboards
                                ▼
                         ┌──────────────┐
              ┌─────────▶│  RESTAURANT  │◀─────────┐
              │          └──────┬───────┘          │
              │                 │                  │
         receives               │ onboards         │ assigns
          orders                │                  │ deliveries
              │                 ▼                  │
              │          ┌──────────────┐          │
              │          │    DRIVER    │──────────┘
              │          └──────┬───────┘
              │                 │
              │                 │ delivers to
              │                 ▼
         ┌────┴────┐     ┌──────────────┐
         │  ORDER  │◀────│   CUSTOMER   │
         └─────────┘     └──────────────┘
                              places
```
