# Phase 1 - Question 1: Modular Monolith + Vertical Slices

**One process, one PostgreSQL database, eight modules.** Matches `FinalAnswers/Question1/Question1.md` and the Q1 diagram.

## Read these first (in order)
1. `src/Host/FoodDelivery.Api/Program.cs` - the whole architecture in one file
2. `src/Modules/Ordering/Features/PlaceOrder/PlaceOrder.cs` - the main use case, step by step
3. `src/Modules/Ordering/Domain/Order.cs` - the state machine every slice obeys
4. `src/Modules/Restaurants/PublicApi/IRestaurantsApi.cs` - what "public API" means
5. `src/Modules/Payments/Provider/` - ports & adapters
6. `tests/FoodDelivery.Tests/ArchitectureTests.cs` - how the module walls are enforced

## Layout
```
src/
  Host/FoodDelivery.Api/        the ONE deployable
  BuildingBlocks/               tiny shared plumbing (no business logic)
  Modules/<Name>/
    PublicApi/                  the ONLY public types (interface + events)
    Features/<UseCase>/         one folder per vertical slice
    Domain/                     shared rules inside the module (Ordering only)
    Data/schema.sql             this module's own schema
    <Name>Module.cs             registration
tests/                          architecture tests + order-rule tests
```

## Known Q1 weaknesses (deliberate - they set up Q2 and Q3)
- Notifications are sent **inside** the request (~300 ms extra): see `PlaceOrder.cs` (Q2 problem P3)
- A payment **timeout is recorded as FAILED**: see `Payments/Features/Authorise/Authorise.cs` (Q3's Friday incident)
- Events are **in memory**: lost if the process crashes (`BuildingBlocks/Events.cs`)

Run: `docker compose up -d`, then `dotnet run --project src/Host/FoodDelivery.Api`, then `requests.http`.
