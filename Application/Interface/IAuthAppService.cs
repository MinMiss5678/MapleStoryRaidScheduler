using Application.DTOs;

namespace Application.Interface;

public interface IAuthAppService
{
    Task<LoginResult> LoginAsync(string code);
    Task<bool> LogoutAsync(string sessionId, string discordId);
}
