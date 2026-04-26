using System.Security.Claims;
using System.Text;
using KindleKeep.Api.Core.Entities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KindleKeep.Api.Infrastructure.Identity;

public class TokenService(IConfiguration configuration)
{
    public string GenerateToken(User user)
    {
        // Retrieve the key from the Secret Manager (or Environment Variables in production)
        var secretKey = configuration["Jwt:Key"] 
            ?? throw new InvalidOperationException("JWT Secret Key is missing from configuration.");
            
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Define the claims payload
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim("avatar", user.AvatarUrl)
            ]),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = credentials,
            Issuer = "KindleKeep-Auth",
            Audience = "KindleKeep-Dashboard"
        };

        var handler = new JsonWebTokenHandler();
        
        // Generates the stateless, cryptographically signed string
        return handler.CreateToken(descriptor); 
    }
}