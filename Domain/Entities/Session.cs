namespace Domain.Entities;

public class Session
{
    public required string SessionId { get; set; }
    public ulong DiscordId { get; set; }
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTimeOffset Expiry { get; set; }              // AccessToken 過期（token metadata，登入時設）
    public DateTimeOffset SessionExpiry { get; set; }       // session 有效期（我的授權政策，與 Discord token 解耦）
}
