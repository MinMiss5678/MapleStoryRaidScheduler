using Domain.Entities;

namespace Application.Interface;

public interface IJwtService
{
    string CreateToken(DiscordUser discordUser, int expireMinutes = 15);
    JwtValidationResult ValidateToken(string token);
    JwtTokenClaims ReadJsonWebToken(string token);
}