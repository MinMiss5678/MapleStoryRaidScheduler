using System.Security.Claims;
using System.Text;
using Application.Interface;
using Application.Options;
using Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

public class JwtService : IJwtService
{
    private readonly JwtOptions _jwtOptions;

    public JwtService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public string CreateToken(DiscordUser discordUser, int expireMinutes = 15)
    {
        var claims = new List<Claim>
        {
            new Claim("discordId", discordUser.Id.ToString()),
        };

        var expiration = DateTime.UtcNow.AddMinutes(expireMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            Expires = expiration,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey)),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(descriptor);

        return token;
    }

    public JwtValidationResult ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtOptions.Audience,
            ValidateLifetime = true,
            IssuerSigningKey = key
        };

        var handler = new JsonWebTokenHandler();
        var validateToken = handler.ValidateTokenAsync(token, parameters).GetAwaiter().GetResult();
        if (!validateToken.IsValid)
        {
            return new JwtValidationResult()
            {
                IsValid = validateToken.IsValid,
                Exception = validateToken.Exception,
            };
        }
        else
        {
            return new JwtValidationResult()
            {
                DiscordId = Convert.ToUInt64(validateToken.Claims.FirstOrDefault(c => c.Key == "discordId").Value),
                IsValid = validateToken.IsValid,
                Exception = validateToken.Exception
            };
        }
    }

    public JwtTokenClaims ReadJsonWebToken(string token)
    {
        var handler = new JsonWebTokenHandler();
        var jwtToken = handler.ReadJsonWebToken(token);

        return new JwtTokenClaims()
        {
            DiscordId = Convert.ToUInt64(jwtToken.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value),
        };
    }
}