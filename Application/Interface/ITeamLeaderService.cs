using Application.DTOs;

namespace Application.Interface;

/// <summary>隊長主導組隊（leader-led）。Phase 1b：開隊 + 設條件（寫路徑，無併發）。</summary>
public interface ITeamLeaderService
{
    /// <summary>隊長開隊 + 條件，回新隊 id。</summary>
    Task<int> CreateTeamAsync(CreateTeamCommand command);

    /// <summary>Pull：某隊符合條件的候選（時段重疊 + 職業/攻擊 + 通關數；回能力欄、不含 discord 身分）。</summary>
    Task<IEnumerable<TeamCandidateDto>> GetCandidatesAsync(int teamSlotId);
}
