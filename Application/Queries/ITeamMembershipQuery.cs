using Application.DTOs;

namespace Application.Queries;

/// <summary>leader-led 玩家/隊長端的讀查詢（發現/自助）。</summary>
public interface ITeamMembershipQuery
{
    /// <summary>某玩家某狀態的成員關係（Invited=我的邀請、Confirmed=我的隊）。</summary>
    Task<IEnumerable<MembershipDto>> GetByDiscordIdAndStatusAsync(ulong discordId, string status);

    /// <summary>某隊的申請佇列（Applied）——隊長審核用。</summary>
    Task<IEnumerable<MembershipDto>> GetApplicationsAsync(int teamSlotId);

    /// <summary>尚有空位的 leader 開放隊（含條件）——玩家 Push 發現用；排除 currentDiscordId 自己開的隊。</summary>
    Task<IEnumerable<OpenTeamDto>> GetOpenTeamsAsync(ulong currentDiscordId);

    /// <summary>某隊長某週期開的隊（含 confirmed/applied/invited 計數）——隊長 hub 導覽用。</summary>
    Task<IEnumerable<LedTeamDto>> GetLedTeamsAsync(ulong leaderDiscordId);

    /// <summary>玩家收到的待處理隊長轉讓（PendingLeaderDiscordId=本人）。</summary>
    Task<IEnumerable<LeaderTransferDto>> GetPendingLeaderTransfersAsync(ulong discordId);

    /// <summary>某隊 Confirmed 成員名冊（memberId + 顯示名 + 職業，排除 excludeDiscordId 自己）——隊長轉讓挑人用。</summary>
    Task<IEnumerable<RosterMemberDto>> GetRosterAsync(int teamSlotId, ulong excludeDiscordId);

    /// <summary>某隊 Confirmed 成員組成（角色 + 職業 + 是否隊長，不含 Discord 身分）——已入隊成員看隊友用。</summary>
    Task<IEnumerable<TeamMemberDto>> GetConfirmedMembersAsync(int teamSlotId);

    /// <summary>單隊的需求列（職業組 + 數量）——算招募缺口用。</summary>
    Task<IEnumerable<OpenTeamRequirementDto>> GetRequirementsAsync(int teamSlotId);

    /// <summary>單隊所有 Confirmed 成員的職業（可重複）——算招募缺口用。</summary>
    Task<IEnumerable<string>> GetConfirmedJobsAsync(int teamSlotId);
}
