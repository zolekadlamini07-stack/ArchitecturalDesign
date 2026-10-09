using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Identity.Data;
using FoodDelivery.Modules.Identity.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Identity.Features.RegisterCustomer;

// ============================================================================================
// VERTICAL SLICE: "Register as a customer"   (brief: Customers - "Create an account")
//
// A slice holds EVERYTHING for one use case, top to bottom, in one place:
//   1. the HTTP endpoint (route + who may call it)
//   2. the request/response shapes
//   3. the handler (the actual work, including its data access)
// To change how registration works, you open THIS folder and nothing else.
// ============================================================================================
internal static class RegisterCustomer
{
    public sealed record Request(string Email, string Password, string DisplayName);
    public sealed record Response(Guid UserId, string Token);

    // 1. Endpoint: anyone may register (no login needed).
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/identity/register", Handle).AllowAnonymous().WithTags("Identity");

    // 3. Handler.
    private static async Task<IResult> Handle(
        Request request, IdentityDbContext db, TokenService tokens, IEventBus events, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return Problems.Validation(nameof(request.Email), "A valid email is required.");
        if (request.Password is not { Length: >= 8 })
            return Problems.Validation(nameof(request.Password), "Password must be at least 8 characters.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Problems.Conflict("An account with this email already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = Roles.Customer,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // Announce the fact. The Customers module (which Identity knows nothing about) will
        // create an empty profile for this user. In Q1 this runs in-process, right now.
        await events.PublishAsync(new CustomerRegistered(user.Id, user.Email, user.DisplayName), ct);

        return Results.Created($"/identity/users/{user.Id}", new Response(user.Id, tokens.CreateToken(user)));
    }
}
