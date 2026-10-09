using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers;
using FoodDelivery.Modules.Delivery;
using FoodDelivery.Modules.Identity;
using FoodDelivery.Modules.Notifications;
using FoodDelivery.Modules.Ordering;
using FoodDelivery.Modules.Payments;
using FoodDelivery.Modules.Reporting;
using FoodDelivery.Modules.Restaurants;

// =============================================================================================
// QUESTION 2 - WORKER HOST (D13)
//
//   Api process:    slice saves data + event in its OUTBOX (one transaction) -> responds fast
//   Worker process: OutboxDispatcher  - outbox rows -> one job per subscribed handler
//                   JobRunner         - runs each job; retries with backoff; dead-letters failures
//                   module worker jobs - e.g. Reporting's daily summary
// =============================================================================================

var builder = Host.CreateApplicationBuilder(args);

IModule[] modules =
[
    new IdentityModule(),
    new CustomersModule(),
    new RestaurantsModule(),
    new OrderingModule(),
    new PaymentsModule(),
    new DeliveryModule(),
    new NotificationsModule(),
    new ReportingModule(),
];

// The same module registrations as the Api: DbContexts, public APIs and - importantly - the
// event handlers, which the JobRunner resolves by type.
foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
    module.AddWorkerServices(builder.Services, builder.Configuration);
}

builder.Services.AddSingleton<IFeatureFlags, PostgresFeatureFlags>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<JobRunner>();

// The Api creates the schemas on start-up. If the Worker starts first, its loops simply log and
// retry until the tables exist - no start-up ordering needed between the two processes.
builder.Build().Run();
