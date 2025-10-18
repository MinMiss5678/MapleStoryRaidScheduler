namespace Application.DTOs;

public class LoginResult
{
    public string? SessionId { get; set; }
    public string? JwtToken { get; set; }
    public ulong DiscordId { get; set; }
    public DateTimeOffset Expiry { get; set; }

    // 方便判斷
    public bool IsSession => !string.IsNullOrEmpty(SessionId);
    public bool IsJwt => !string.IsNullOrEmpty(JwtToken);
}