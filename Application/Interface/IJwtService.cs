using Domain.Entities;

namespace Application.Interface;

public interface IJwtService
{
    string CreateToken(DiscordUser discordUser, string role, int expireMinutes = 15);
    JwtValidationResult ValidateToken(string token);
    JwtTokenClaims ReadJsonWebToken(string token);
}
