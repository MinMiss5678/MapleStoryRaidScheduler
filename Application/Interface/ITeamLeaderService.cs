using Application.DTOs;

namespace Application.Interface;

/// <summary>隊長主導組隊（leader-led）。Phase 1b：開隊 + 設條件（寫路徑，無併發）。</summary>
public interface ITeamLeaderService
{
    /// <summary>隊長開隊 + 條件，回新隊 id。</summary>
    Task<int> CreateTeamAsync(CreateTeamCommand command);

    /// <summary>Pull：某隊符合條件的候選（時段重疊 + 職業/攻擊 + 通關數；回能力欄、不含 discord 身分）。</summary>
    Task<IEnumerable<TeamCandidateDto>> GetCandidatesAsync(int teamSlotId);

    /// <summary>Pull：隊長邀請候選（→Invited）。重複邀請由 DB unique 擋成 409。</summary>
    Task InviteMemberAsync(int teamSlotId, string characterId, ulong leaderDiscordId);

    /// <summary>玩家接受邀請（Invited→Confirmed）：1002 鎖守容量；跨隊時段重疊由 DB unique→409。</summary>
    Task AcceptInviteAsync(int memberId, ulong currentDiscordId);

    /// <summary>玩家拒絕邀請（→Rejected，xmin 樂觀鎖）。</summary>
    Task DeclineInviteAsync(int memberId, ulong currentDiscordId);
}
