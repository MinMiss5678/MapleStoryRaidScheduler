using Newtonsoft.Json;

namespace Application.DTOs;

public class DiscordUserDto
{
    public ulong Id { get; set; }
    public required string Username { get; set; }

    // 帳號層級顯示名（Discord 新制），可能為 null；作為公會暱稱缺失時的 fallback。
    [JsonProperty("global_name")]
    public string? GlobalName { get; set; }
}
