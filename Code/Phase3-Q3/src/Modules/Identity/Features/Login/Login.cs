using FoodDelivery.Modules.Identity.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Identity.Features.Login;

// VERTICAL SLICE: "Log in" - for every kind of user (customer, restaurant staff, driver, admin).
// The returned token carries the user's role, so the client knows which views to show and every
// module knows what the caller is allowed to do.
internal static class Login
{
    public sealed record Request(string Email, string Password);
    public sealed record Response(string Token, string Role, Guid? RestaurantId);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/identity/login", Handle).AllowAnonymous().WithTags("Identity");

    private static async Task<IResult> Handle(Request request, IdentityDbContext db, TokenService tokens, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // Same answer for "no such user" and "wrong password", so attackers can't discover accounts.
        if (user is null ||
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Results.Unauthorized();

        return Results.Ok(new Response(tokens.CreateToken(user), user.Role, user.RestaurantId));
    }
}
