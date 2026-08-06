using Application.DTOs;

namespace Application.Interface;

/// <summary>隊長主導組隊（leader-led）。Phase 1b：開隊 + 設條件（寫路徑，無併發）。</summary>
public interface ITeamLeaderService
{
    /// <summary>隊長開隊 + 條件，回新隊 id。</summary>
    Task<int> CreateTeamAsync(CreateTeamCommand command);
}
