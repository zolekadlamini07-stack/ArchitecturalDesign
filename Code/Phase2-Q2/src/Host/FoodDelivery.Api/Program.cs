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
using Npgsql;

// =============================================================================================
// QUESTION 2 - API HOST (same modular monolith, now one of TWO hosts)
//
// What changed from Phase 1 (compare the files):
//   - no IEventBus: events go through each module's OUTBOX and are run by the WORKER host
//   - run as 2+ instances behind a load balancer with rolling deploys (D15); /health checks the DB
//   - feature flags (D16), with an admin endpoint to switch them
// What did NOT change: the modules, the vertical slices, the single deployable.
// =============================================================================================

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSingleton<IFeatureFlags, PostgresFeatureFlags>();

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
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

// Q2 (D15): ROLLING DEPLOYS. The platform starts the new version, waits until /health says OK,
// moves traffic to it, then stops the old one. So /health must check what we really need: the DB.
app.MapGet("/health", async (IConfiguration config, CancellationToken ct) =>
{
    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionStringFor("Default"));
        await connection.OpenAsync(ct);
        return Results.Ok(new { status = "ok" });
    }
    catch (Exception ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unreachable", detail: ex.Message);
    }
}).AllowAnonymous();

// Q2 (D16): switch a feature on/off without deploying.
app.MapPut("/admin/feature-flags/{name}", async (string name, bool enabled, IFeatureFlags flags, CancellationToken ct) =>
{
    await flags.SetAsync(name, enabled, ct);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).WithTags("Admin");

foreach (var module in modules)
    module.MapEndpoints(app);

await Persistence.EnsureSchemasAsync(builder.Configuration.ConnectionStringFor("Default"), modules);

app.Run();
