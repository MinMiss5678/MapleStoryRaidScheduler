using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotRepository
{
    Task<int> CreateAsync(TeamSlot teamSlot);
    Task DeleteAsync(int id);
    Task<TeamSlot?> GetByIdAsync(int id);

    /// <summary>設待處理隊長轉讓目標（提議＝設目標、拒絕/作廢＝設 null）。</summary>
    Task SetPendingLeaderAsync(int teamSlotId, ulong? pendingDiscordId);

    /// <summary>完成轉讓：LeaderDiscordId 設為新隊長、清空 pending。</summary>
    Task CompleteLeaderTransferAsync(int teamSlotId, ulong newLeaderDiscordId);
}
