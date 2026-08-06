using Application.DTOs;

namespace Application.Queries;

/// <summary>leader-led 玩家/隊長端的讀查詢（發現/自助）。</summary>
public interface ITeamMembershipQuery
{
    /// <summary>某玩家某狀態的成員關係（Invited=我的邀請、Confirmed=我的隊）。</summary>
    Task<IEnumerable<MembershipDto>> GetByDiscordIdAndStatusAsync(ulong discordId, string status);

    /// <summary>某隊的申請佇列（Applied）——隊長審核用。</summary>
    Task<IEnumerable<MembershipDto>> GetApplicationsAsync(int teamSlotId);

    /// <summary>某週期內尚有空位的 leader 開放隊（含條件）——玩家 Push 發現用。</summary>
    Task<IEnumerable<OpenTeamDto>> GetOpenTeamsAsync(int periodId);
}
