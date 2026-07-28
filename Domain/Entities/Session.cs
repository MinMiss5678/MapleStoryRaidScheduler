namespace Domain.Entities;

public class Session
{
    public required string SessionId { get; set; }
    public ulong DiscordId { get; set; }
    public DateTimeOffset SessionExpiry { get; set; }       // session 有效期（我的授權政策）
}
