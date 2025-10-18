namespace Domain.Entities;

public class Session
{
    public string SessionId { get; set; }
    public ulong DiscordId { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTimeOffset Expiry { get; set; }
}