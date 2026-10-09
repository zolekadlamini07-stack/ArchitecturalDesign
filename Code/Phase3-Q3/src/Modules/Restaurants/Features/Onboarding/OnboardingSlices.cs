using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Restaurants.Caching;
using FoodDelivery.Modules.Restaurants.Data;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Features.Onboarding;

// =============================================================================================
// Q2 CHANGE 7 - SELF-SERVICE ONBOARDING (problem P7: "add more restaurants rapidly").
// No new architecture: two new SLICES in an existing module, plus one event.
// This is vertical slice architecture paying off - a new feature is new folders, not edits
// scattered across shared layers.
// It ships behind a FEATURE FLAG (D16): deployed switched off, released when the business is ready.
// =============================================================================================

/// <summary>SLICE: "A restaurant applies to join the platform" (anonymous).</summary>
internal static class ApplyAsRestaurant
{
    public sealed record Request(string Name, string Address, decimal DeliveryFee, string ContactEmail, string Currency = "ZAR");
    public sealed record Response(Guid ApplicationId, string Status);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/restaurants/applications", Handle).AllowAnonymous().WithTags("Onboarding");

    private static async Task<IResult> Handle(Request request, RestaurantsDbContext db, IFeatureFlags flags, CancellationToken ct)
    {
        // Feature switched off -> behave as if the endpoint doesn't exist yet.
        if (!await flags.IsEnabledAsync(FeatureFlagNames.SelfServiceOnboarding, ct)) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Name) || !request.ContactEmail.Contains('@'))
            return Problems.Validation(nameof(request.Name), "A name and a valid contact email are required.");

        var restaurant = new Restaurant
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), Address = request.Address.Trim(), DeliveryFee = request.DeliveryFee,
            Currency = request.Currency, IsOpen = false, ApprovalStatus = ApprovalStatus.PendingApproval,
            ContactEmail = request.ContactEmail.Trim(), CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync(ct);
        return Results.Accepted($"/restaurants/applications/{restaurant.Id}", new Response(restaurant.Id, restaurant.ApprovalStatus));
    }
}

/// <summary>
/// SLICE: "Admin approves an application".
/// Approval and the RestaurantApproved event are saved in ONE transaction (outbox), so we can never
/// end up with an approved restaurant that never gets a login.
/// </summary>
internal static class ApproveRestaurant
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/restaurants/applications/{restaurantId:guid}/approve", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin)).WithTags("Onboarding");

    private static async Task<IResult> Handle(Guid restaurantId, RestaurantsDbContext db, RestaurantCache cache, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.SingleOrDefaultAsync(r => r.Id == restaurantId, ct);
        if (restaurant is null) return Problems.NotFound("Application");
        if (restaurant.ApprovalStatus == ApprovalStatus.Active) return Results.NoContent(); // already approved: idempotent

        restaurant.ApprovalStatus = ApprovalStatus.Active;
        db.AddToOutbox(new RestaurantApproved(restaurant.Id, restaurant.Name, restaurant.ContactEmail!));
        await db.SaveChangesAsync(ct); // restaurant + event, atomically

        await cache.InvalidateListAsync(ct);
        await cache.InvalidateMenuAsync(restaurant.Id, ct);
        return Results.NoContent();
    }
}
