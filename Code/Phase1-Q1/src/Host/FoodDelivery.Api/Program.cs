using System.Text;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers;
using FoodDelivery.Modules.Delivery;
using FoodDelivery.Modules.Identity;
using FoodDelivery.Modules.Notifications;
using FoodDelivery.Modules.Ordering;
using FoodDelivery.Modules.Payments;
using FoodDelivery.Modules.Reporting;
using FoodDelivery.Modules.Restaurants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// =============================================================================================
// QUESTION 1 - THE MODULAR MONOLITH HOST
//
// One process, one deployable, one database. Eight modules plugged in below.
// Read this file top to bottom and you have the whole Q1 architecture diagram (page 1):
//   clients --HTTPS/JSON--> [this app: 8 modules] --> PostgreSQL (one schema per module)
//                                       |--> payment provider (via adapter)
//                                       |--> push/email provider (via adapter)
// =============================================================================================

var builder = WebApplication.CreateBuilder(args);

// ---- 1. The modules. Adding a capability = adding a module to this list. ----------------------
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

foreach (var module in modules)
    module.AddServices(builder.Services, builder.Configuration);

// ---- 2. Shared plumbing (BuildingBlocks) --------------------------------------------------------
// Q1: events are delivered IN-PROCESS, in memory, during the request (D5). Q2 makes them durable.
builder.Services.AddScoped<IEventBus, InProcessEventBus>();

// ---- 3. Authentication: every module trusts the token Identity issues --------------------------
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
var jwt = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
    });
builder.Services.AddAuthorization();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(); // machine-readable description of every endpoint at /openapi/v1.json

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

// Health check: the PaaS pings this to know the app is alive (and restarts it if not).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// ---- 4. Each module maps its own endpoints - one per vertical slice --------------------------
foreach (var module in modules)
    module.MapEndpoints(app);

// ---- 5. Each module creates its own schema (IF NOT EXISTS) before we accept traffic ----------
await Persistence.EnsureSchemasAsync(builder.Configuration.ConnectionStringFor("Default"), modules);

app.Run();
