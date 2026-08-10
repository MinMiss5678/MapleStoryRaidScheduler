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

    /// <summary>Push：玩家申請入隊（→Applied，須用本人角色）。重複申請由 DB unique 擋成 409。</summary>
    Task ApplyAsync(int teamSlotId, string characterId, ulong applicantDiscordId);

    /// <summary>Push：隊長核准申請（Applied→Confirmed）：1002 鎖守容量；跨隊時段重疊由 DB unique→409。</summary>
    Task ApproveAsync(int memberId, ulong leaderDiscordId);

    /// <summary>Push：隊長拒絕申請（→Rejected，xmin 樂觀鎖）。</summary>
    Task RejectAsync(int memberId, ulong leaderDiscordId);

    /// <summary>玩家收到的待處理邀請（Pull 玩家端發現）。</summary>
    Task<IEnumerable<MembershipDto>> GetMyInvitationsAsync(ulong discordId);

    /// <summary>玩家已入隊的隊（Confirmed，跨隊行程用）。</summary>
    Task<IEnumerable<MembershipDto>> GetMyTeamsAsync(ulong discordId);

    /// <summary>某隊的申請佇列（隊長審核；驗隊長擁有）。</summary>
    Task<IEnumerable<MembershipDto>> GetApplicationsAsync(int teamSlotId, ulong leaderDiscordId);

    /// <summary>本期尚有空位的 leader 開放隊（Push 玩家端發現）。</summary>
    Task<IEnumerable<OpenTeamDto>> GetOpenTeamsAsync();

    /// <summary>本期我當隊長開的隊（含各狀態計數，隊長 hub 入口）。</summary>
    Task<IEnumerable<LedTeamDto>> GetLedTeamsAsync(ulong leaderDiscordId);

    /// <summary>玩家自助退隊（Confirmed→Left，釋放位子、可重邀）：只能退自己在該隊的成員資格；xmin 樂觀鎖；通知隊長。</summary>
    Task LeaveTeamAsync(int teamSlotId, ulong currentDiscordId);

    /// <summary>隊長提議把隊長轉給某 Confirmed 成員（→PendingLeaderDiscordId，需對方接受）。只有隊長能提議。</summary>
    Task ProposeLeaderTransferAsync(int teamSlotId, int memberId, ulong currentDiscordId);

    /// <summary>被指定者回應轉讓 accept（→成為新隊長）/ decline；通知原隊長。</summary>
    Task RespondLeaderTransferAsync(int teamSlotId, ulong currentDiscordId, string action);

    /// <summary>我收到的待處理隊長轉讓（收件匣）。</summary>
    Task<IEnumerable<LeaderTransferDto>> GetMyLeaderTransfersAsync(ulong discordId);

    /// <summary>某隊 Confirmed 名冊（隊長轉讓挑人；驗隊長擁有）。</summary>
    Task<IEnumerable<RosterMemberDto>> GetTeamRosterAsync(int teamSlotId, ulong leaderDiscordId);
}
