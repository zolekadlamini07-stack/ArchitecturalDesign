# Phase 2 - Question 2: Same Monolith, Targeted Additions

Started as a copy of Phase 1. **Same modules, same slices.** Matches `FinalAnswers/Question2/Question2.md`.

## What changed, and where (compare each file with Phase 1)
| Q2 problem | Change | Files |
|-----------|--------|-------|
| P3 notifications slow requests | Outbox + PostgreSQL job queue + **Worker process** | `BuildingBlocks/Outbox.cs`, `BuildingBlocks/Jobs.cs`, `src/Host/FoodDelivery.Worker/` |
| P3 (cont.) | Notifications now LISTEN to order events; Ordering no longer calls it | `Modules/Notifications/NotificationsModule.cs`, `Ordering/Features/PlaceOrder/PlaceOrder.cs` |
| P1 browsing traffic | Redis cache-aside + invalidation; filters + paging | `Modules/Restaurants/Caching/`, `Restaurants/Features/Browse/` |
| P4 reporting slows orders | Read replica connection + daily summary job (Worker only) | `Modules/Reporting/ReportingModule.cs` |
| P5 disruptive deploys | `/health` checks the DB; feature flags; CI example | `Host/FoodDelivery.Api/Program.cs`, `BuildingBlocks/FeatureFlags.cs`, `.github/workflows/ci.yml` |
| P6 developers collide | DB role per module, CODEOWNERS | `db-roles.sql`, `.github/CODEOWNERS` |
| P7 add restaurants fast | Self-service onboarding (behind a flag) | `Modules/Restaurants/Features/Onboarding/` |

Measured end to end: **PlaceOrder dropped from ~590 ms (Phase 1) to ~195 ms**, because the notification left the request.

## Run (two processes now)
```bash
docker compose up -d
dotnet run --project src/Host/FoodDelivery.Api       # terminal 1
dotnet run --project src/Host/FoodDelivery.Worker    # terminal 2
```
Then `requests.http`. Watch the Worker console: notifications and event handlers appear there.
