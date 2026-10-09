using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers.Data;
using FoodDelivery.Modules.Identity.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Customers.Features.Profile;

// Customers is a simple, CRUD-like module, so its slices are simple too. That is the point of
// vertical slices: each slice is only as complex as its job (no forced layers, no domain classes
// where there are no business rules).

/// <summary>SLICE: "View my profile".</summary>
internal static class GetMyProfile
{
    public sealed record Response(Guid CustomerId, string DisplayName, string? Phone);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/customers/me", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Customers");

    private static async Task<IResult> Handle(ClaimsPrincipal user, CustomersDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.CustomerId == user.UserId(), ct);
        return profile is null
            ? Problems.NotFound("Profile")
            : Results.Ok(new Response(profile.CustomerId, profile.DisplayName, profile.Phone));
    }
}

/// <summary>SLICE: "Update my profile".</summary>
internal static class UpdateMyProfile
{
    public sealed record Request(string DisplayName, string? Phone);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/customers/me", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Customers");

    private static async Task<IResult> Handle(Request request, ClaimsPrincipal user, CustomersDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.CustomerId == user.UserId(), ct);
        if (profile is null) return Problems.NotFound("Profile");
        profile.DisplayName = request.DisplayName.Trim();
        profile.Phone = request.Phone;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>
/// EVENT HANDLER SLICE: "When someone registers, give them a profile".
/// Subscribes to Identity's PUBLIC event. Identity never calls Customers; it just announces
/// CustomerRegistered and this handler reacts. (Q1: runs in-process inside the register request.)
/// </summary>
internal sealed class CreateProfileWhenCustomerRegisters(CustomersDbContext db) : IDomainEventHandler<CustomerRegistered>
{
    public async Task HandleAsync(CustomerRegistered e, CancellationToken ct)
    {
        if (await db.Profiles.AnyAsync(p => p.CustomerId == e.UserId, ct)) return; // already done: safe to repeat
        db.Profiles.Add(new CustomerProfile { CustomerId = e.UserId, DisplayName = e.DisplayName, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }
}
