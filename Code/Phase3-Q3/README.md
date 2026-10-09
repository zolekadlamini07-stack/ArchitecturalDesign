# Phase 3 - Question 3 (Friday Night) + Final Challenge

Started as a copy of Phase 2. **Same topology, same infrastructure.** The Friday fixes live inside the Ordering and Payments modules. Matches `FinalAnswers/Question3/Question3.md` and `FinalAnswers/Challenge/Challenge.md`.

## Read these first
1. `src/Modules/Ordering/Features/PlaceOrder/PlaceOrder.cs` - order first, 202, idempotency key, one pending order
2. `src/Modules/Payments/Features/Authorise/AuthoriseOnOrderPlaced.cs` - **the Friday fix, line by line**
3. `src/Modules/Payments/Provider/ResilientPaymentProvider.cs` - timeout + circuit breaker
4. `src/Modules/Payments/Features/Reconciliation/` - webhook, sweeper, daily settlement
5. `src/Modules/Ordering/Features/PaymentOutcome/PaymentOutcomeSlices.cs` - late success, so release the hold
6. `src/Modules/PartnerIntegration/` - the anti-corruption layer (challenge)

## Q3 walkthrough (Question3.md section 5) mapped to code
| Scenario | What the code does | Where |
|----------|-------------------|-------|
| Payment request times out | Attempt becomes **UNKNOWN**, not failed | `AuthoriseOnOrderPlaced.cs` |
| It actually succeeded | Webhook / sweeper finds it, order proceeds | `ReceiveProviderWebhook.cs`, `ReconciliationSweeper.cs` |
| Customer retries | Same key returns the same order; new key returns 409 | `PlaceOrder.cs` + `ordering` schema index |
| Same request twice | One live attempt per order + provider idempotency key | `payments` schema, `FakePaymentProvider.cs` / `StripePaymentProvider.cs` |
| Crash after success | Status + event saved in one transaction (outbox); job lease re-runs with the same key | `AttemptOutcome.cs`, `BuildingBlocks/Jobs.cs` |
| Provider back online | Breaker HALF-OPEN then CLOSED; sweeper replays with the same key | `ResilientPaymentProvider.cs`, `ReconciliationSweeper.cs` |
| Retries | Backoff + jitter; open circuit = "retry later" without using up an attempt; dead letter | `BuildingBlocks/Jobs.cs` |
| What happened? | Append-only payment log + support timeline | `Payments/Data/`, `GetOrderTimeline.cs` |

## Verified end to end (against real PostgreSQL + Redis)
- Normal order: 202 → authorised in the Worker → restaurant accepts → captured in the background.
- **Timeout but succeeded:** UNKNOWN → customer sees "Confirming your payment" → a retry with a new key gets 409 → webhook resolves it → **provider charged exactly once**.
- **Provider down:** browsing unaffected; once the provider is back, the order confirms by itself.
- **Challenge:** partner catalogue synced and translated; partner order paid, forwarded, and driven by their webhooks to Delivered; unknown partner status parked as an alert.

## Simulate Friday night
```
PUT /admin/feature-flags/fake-provider-timeout-but-succeeds?enabled=true
PUT /admin/feature-flags/fake-provider-down?enabled=true
```
(see `requests.http`, sections F and C)
