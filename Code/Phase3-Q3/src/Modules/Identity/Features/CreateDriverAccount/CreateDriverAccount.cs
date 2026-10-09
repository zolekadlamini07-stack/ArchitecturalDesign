using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Identity.Data;
using FoodDelivery.Modules.Identity.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Identity.Features.CreateDriverAccount;

// ============================================================================================
// VERTICAL SLICE: "Restaurant onboards one of its drivers".
//
// D7 - DRIVERS BELONG TO RESTAURANTS. The restaurant creates the driver's login, and the driver
// is permanently linked to THAT restaurant. Note the restaurant id comes from the STAFF MEMBER'S
// TOKEN, not from the request body - a restaurant can only ever create drivers for itself.
// ============================================================================================
internal static class CreateDriverAccount
{
    public sealed record Request(string Email, string Password, string DisplayName);
    public sealed record Response(Guid DriverUserId);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/identity/drivers", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff))
           .WithTags("Identity");

    private static async Task<IResult> Handle(
        Request request, ClaimsPrincipal staff, IdentityDbContext db, CancellationToken ct)
    {
        var restaurantId = staff.RestaurantId();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Problems.Conflict("An account with this email already exists.");

        var driver = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = Roles.Driver,
            RestaurantId = restaurantId,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        driver.PasswordHash = new PasswordHasher<User>().HashPassword(driver, request.Password);
        db.Users.Add(driver);
        // Q2: published via the outbox, atomically with the new user. Delivery creates the profile later, in the Worker.
        db.AddToOutbox(new DriverAccountCreated(driver.Id, restaurantId, driver.DisplayName));
        await db.SaveChangesAsync(ct);

        return Results.Created($"/identity/users/{driver.Id}", new Response(driver.Id));
    }
}
