using System.Security.Claims;

namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// The four kinds of user. The Identity module puts the role (and, for staff and drivers, the
/// restaurant id) INTO the login token. Every other module trusts the token and never looks at
/// passwords - that is Identity's job alone.
/// </summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string RestaurantStaff = "RestaurantStaff";
    public const string Driver = "Driver";
    public const string Admin = "Admin";
}

/// <summary>Custom claim names placed in the token by Identity.</summary>
public static class AppClaims
{
    public const string RestaurantId = "restaurant_id";
}

/// <summary>Convenience readers for the current user, used by slices.</summary>
public static class CurrentUser
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new UnauthorizedAccessException("No user id in token."));

    /// <summary>
    /// Restaurant staff and drivers belong to exactly one restaurant (D7: drivers are owned by
    /// restaurants). The token carries that restaurant id, so a staff member can only ever act on
    /// their own restaurant's orders and menu - the CLIENT never chooses it (trust boundary B5).
    /// </summary>
    public static Guid RestaurantId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(AppClaims.RestaurantId)
                   ?? throw new UnauthorizedAccessException("This user does not belong to a restaurant."));
}
