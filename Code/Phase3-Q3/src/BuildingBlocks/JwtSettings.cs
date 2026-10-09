namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// Settings for the login token (a JWT). Identity CREATES tokens with these settings;
/// the host VALIDATES them on every request. Bound from the "Jwt" section of appsettings.json.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer { get; init; } = "food-delivery";
    public string Audience { get; init; } = "food-delivery-clients";
    /// <summary>Development-only key. In production this comes from a secret store, never source control.</summary>
    public string SigningKey { get; init; } = "dev-only-signing-key-change-me-0123456789abcdef";
    public int ExpiryMinutes { get; init; } = 120;
}
