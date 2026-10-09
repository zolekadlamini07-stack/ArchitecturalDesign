using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Identity.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Identity.Features.CreateRestaurantStaffAccount;

// VERTICAL SLICE: "Platform admin creates a login for a restaurant's staff".
// In Q1 the PLATFORM onboards the restaurant by hand (there is only one restaurant).
// Q2 adds self-service onboarding because the business wants to add restaurants rapidly.
internal static class CreateRestaurantStaffAccount
{
    public sealed record Request(Guid RestaurantId, string Email, string Password, string DisplayName);
    public sealed record Response(Guid UserId);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/identity/restaurant-staff", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin))
           .WithTags("Identity");

    private static async Task<IResult> Handle(Request request, IdentityDbContext db, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Problems.Conflict("An account with this email already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = Roles.RestaurantStaff,
            RestaurantId = request.RestaurantId, // just an ID - Identity never queries the restaurants schema
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/identity/users/{user.Id}", new Response(user.Id));
    }
}
