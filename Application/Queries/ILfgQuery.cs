using Application.DTOs;

namespace Application.Queries;

/// <summary>即時看板讀取（period-less §8 Phase 3）：列出未過期的找隊意圖。</summary>
public interface ILfgQuery
{
    Task<IEnumerable<LfgBoardItemDto>> GetBoardAsync(ulong currentDiscordId);
}
