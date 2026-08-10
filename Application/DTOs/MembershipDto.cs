namespace Application.DTOs;

/// <summary>
/// 玩家的一筆成員關係（供「我的邀請」/「我的隊」/ 隊長審核佇列）。
/// 回能力欄，不含其他人的 discord 身分（§9.12）——供本人或隊長看。
/// </summary>
public class MembershipDto
{
    public int MemberId { get; set; }
    public int TeamSlotId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string? Job { get; set; }
    public int AttackPower { get; set; }
    public string Status { get; set; } = "";
    public int RequireMembers { get; set; }   // 隊伍容量（Boss.RequireMembers）——供前端顯示 confirmed/require、判斷是否已滿
    public int ConfirmedCount { get; set; }    // 已入隊真實成員數（占容量者）
}

/// <summary>
/// 隊長「我開的隊」清單一列（leader hub 入口）：王/時段/容量 + 三種狀態計數，
/// 供隊長導覽到候選/審核。只列本期、`LeaderDiscordId=本人` 的隊。
/// </summary>
public class LedTeamDto
{
    public int TeamSlotId { get; set; }
    public int BossId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public int RequireMembers { get; set; }
    public int ConfirmedCount { get; set; }   // 已入隊（占容量）
    public int AppliedCount { get; set; }     // 待審核申請（Push）
    public int InvitedCount { get; set; }      // 待玩家回覆的邀請（Pull）
    public string? Description { get; set; }
}

/// <summary>玩家收到的待處理隊長轉讓（Me/LeaderTransfers）：王/時段，供接受/拒絕。</summary>
public class LeaderTransferDto
{
    public int TeamSlotId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
}

/// <summary>某隊 Confirmed 成員一列（隊長轉讓挑人用）：memberId 當轉讓目標（不外流 raw discordId）+ 顯示名。</summary>
public class RosterMemberDto
{
    public int MemberId { get; set; }
    public string? CharacterName { get; set; }
    public string? DiscordName { get; set; }
}

/// <summary>玩家可申請的開放隊（Push 發現）：王/時段/容量/剩餘 + 條件，供玩家判斷是否申請。</summary>
public class OpenTeamDto
{
    public int TeamSlotId { get; set; }
    public int BossId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public int RequireMembers { get; set; }
    public int ConfirmedCount { get; set; }
    public string? Description { get; set; }
    public List<OpenTeamRequirementDto> Requirements { get; set; } = [];
}

public class OpenTeamRequirementDto
{
    public int Count { get; set; }
    public int MinClearCount { get; set; }
    public List<OpenTeamRequirementJobDto> Jobs { get; set; } = [];
}

public class OpenTeamRequirementJobDto
{
    public required string Job { get; set; }
    public int MinAttackPower { get; set; }
}
