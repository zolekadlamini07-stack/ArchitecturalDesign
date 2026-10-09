using System.Security.Cryptography;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Identity.Data;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Identity.Features.CreateStaffForApprovedRestaurant;

/// <summary>
/// Q2 NEW - EVENT HANDLER SLICE: "A restaurant that applied by itself was approved - give it a login".
/// (Q2 change 7: the business wants to add restaurants rapidly, so onboarding becomes self-service.)
///
/// Runs in the WORKER as a job. It may run twice (at-least-once delivery), so it checks first:
/// if the account already exists, it does nothing. That is what "idempotent" means in practice.
/// </summary>
internal sealed class CreateStaffForApprovedRestaurant(IdentityDbContext db, ILogger<CreateStaffForApprovedRestaurant> logger)
    : IDomainEventHandler<RestaurantApproved>
{
    public async Task HandleAsync(RestaurantApproved e, CancellationToken ct)
    {
        var email = e.ContactEmail.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return; // already done

        var staff = new User
        {
            Id = Guid.NewGuid(), Email = email, Role = Roles.RestaurantStaff, RestaurantId = e.RestaurantId,
            DisplayName = e.RestaurantName, CreatedAt = DateTimeOffset.UtcNow,
        };
        // A random password nobody knows. In production we would email a "set your password" link.
        staff.PasswordHash = new PasswordHasher<User>().HashPassword(staff, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
        db.Users.Add(staff);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created staff login {Email} for approved restaurant {RestaurantId} (invitation email would be sent here).", email, e.RestaurantId);
    }
}
