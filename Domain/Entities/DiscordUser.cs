namespace Domain.Entities;

public class DiscordUser
{
    public ulong Id { get; set; }
    public required string Name { get; set; }   // 顯示名（登入時決定＝公會暱稱 ?? global_name ?? username）
    public string? GlobalName { get; set; }      // 帳號層級顯示名，nick 缺失時的 fallback
}
