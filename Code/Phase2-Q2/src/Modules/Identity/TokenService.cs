using System.Security.Claims;
using System.Text;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Identity.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FoodDelivery.Modules.Identity;

/// <summary>
/// Creates the login token. The token carries: who you are, your ROLE, and (for staff and drivers)
/// your RESTAURANT. Every other module reads these claims instead of asking Identity.
/// </summary>
internal sealed class TokenService(IOptions<JwtSettings> settings)
{
    public string CreateToken(User user)
    {
        var s = settings.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(ClaimTypes.Name, user.DisplayName),
        };
        if (user.RestaurantId is { } restaurantId)
            claims.Add(new Claim(AppClaims.RestaurantId, restaurantId.ToString()));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = s.Issuer,
            Audience = s.Audience,
            Expires = DateTime.UtcNow.AddMinutes(s.ExpiryMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s.SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
