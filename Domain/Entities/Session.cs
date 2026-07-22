namespace Domain.Entities;

public class Session
{
    public required string SessionId { get; set; }
    public ulong DiscordId { get; set; }
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTimeOffset Expiry { get; set; }
}
