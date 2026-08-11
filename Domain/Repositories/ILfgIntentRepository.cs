using Domain.Entities;

namespace Domain.Repositories;

/// <summary>即時找隊意圖（period-less §8 Phase 3）的寫入/清理。看板/候選讀取走 ITeamCandidateQuery / 專屬 query。</summary>
public interface ILfgIntentRepository
{
    Task CreateAsync(LfgIntent intent);
    /// <summary>刪自己的一筆意圖（IDOR：WHERE DiscordId AND Id）。回傳影響列數。</summary>
    Task<int> DeleteAsync(ulong discordId, int id);
    /// <summary>清掉某玩家全部意圖（入隊後不再找隊）。</summary>
    Task DeleteByDiscordIdAsync(ulong discordId);
}
