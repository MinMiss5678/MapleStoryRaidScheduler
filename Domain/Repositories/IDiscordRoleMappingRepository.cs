namespace Domain.Repositories;

public interface IDiscordRoleMappingRepository
{
    /// <summary>
    /// 依據 Discord 身分組 ID 清單，回傳對應到系統內最高優先權的 Role（若找不到則回傳 null）
    /// </summary>
    Task<string?> ResolveRoleAsync(IEnumerable<ulong> discordRoleIds);
}