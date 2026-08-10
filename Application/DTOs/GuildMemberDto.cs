namespace Application.DTOs;

/// <summary>公會成員資訊（bot token 查 guilds/{guild}/members/{user}）：身分組 + 公會暱稱。</summary>
public class GuildMemberDto
{
    public IReadOnlyList<string> Roles { get; set; } = [];
    public string? Nick { get; set; }   // 公會暱稱，可能為 null（未設）
}
