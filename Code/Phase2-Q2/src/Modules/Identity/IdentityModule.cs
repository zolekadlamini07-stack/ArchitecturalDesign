using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Identity.Data;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Identity;

/// <summary>
/// The Identity module's registration. This class and the PublicApi folder are the ONLY public
/// things in the project. The host calls these three members; everything else is hidden.
/// </summary>
public sealed class IdentityModule : IModule
{
    public string Name => "identity";

    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(IdentityModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<TokenService>();

        // Q2: Identity publishes CustomerRegistered / DriverAccountCreated through its outbox...
        services.AddOutboxFor(Name);
        // ...and subscribes to Restaurants' RestaurantApproved (self-service onboarding).
        services.AddEventHandler<RestaurantApproved, Features.CreateStaffForApprovedRestaurant.CreateStaffForApprovedRestaurant>();
        services.AddHostedService<AdminSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        Features.RegisterCustomer.RegisterCustomer.Map(app);
        Features.Login.Login.Map(app);
        Features.CreateRestaurantStaffAccount.CreateRestaurantStaffAccount.Map(app);
        Features.CreateDriverAccount.CreateDriverAccount.Map(app);
    }
}

/// <summary>Creates a platform admin on first start so the demo can onboard a restaurant (dev convenience).</summary>
internal sealed class AdminSeeder(IServiceProvider services, IConfiguration configuration, ILogger<AdminSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Q2: both the Api and the Worker load this module; if both try to seed at once, or the
        // tables are not created yet, just log and carry on - seeding is a dev convenience.
        try { await SeedAsync(ct); }
        catch (Exception ex) { logger.LogInformation("Admin seed skipped: {Reason}", ex.Message); }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var email = configuration["Seed:AdminEmail"] ?? "admin@fooddelivery.local";
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return;

        var admin = new User
        {
            Id = Guid.NewGuid(), Email = email, Role = Roles.Admin, DisplayName = "Platform Admin",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, configuration["Seed:AdminPassword"] ?? "Admin123!");
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
