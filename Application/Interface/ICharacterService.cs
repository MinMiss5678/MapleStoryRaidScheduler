using Application.DTOs;

namespace Application.Interface;

public interface ICharacterService
{
    Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null);
    Task<int> CreateAsync(CharacterRequest request);
    Task UpdateAsync(CharacterRequest request);
    Task DeleteAsync(ulong discordId, string id);

    // per 角色 per 王 通關數（玩家自填，取代舊 register 退場後缺的輸入路徑）
    Task<IEnumerable<BossClearDto>> GetBossClearsAsync(ulong discordId, string characterId);
    Task SaveBossClearsAsync(ulong discordId, string characterId, IEnumerable<BossClearDto> clears);

    Task<IEnumerable<int>> GetPreferredBossesAsync(ulong discordId, string characterId);
    Task SavePreferredBossesAsync(ulong discordId, string characterId, IEnumerable<int> bossIds);
}
