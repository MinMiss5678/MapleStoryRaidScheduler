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

/// <summary>隊長審核佇列的一筆申請（Push）：認「人」（DiscordName）+ 決策所需能力（攻擊/通關/祝福）。
/// 與 <see cref="MembershipDto"/>（本人自視：我的邀請／我的隊）分開，避免「欄位只有某情境有值」的不誠實契約。</summary>
public class ApplicantDto
{
    public int MemberId { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string? DiscordName { get; set; }
    public string? Job { get; set; }
    public int AttackPower { get; set; }
    public int BossClearCount { get; set; }     // 該玩家本王總通關（跨其角色加總）
    public int MapleBlessingLevel { get; set; }
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

/// <summary>隊伍組成一列（已入隊成員看隊友用）：角色 + 職業 + 是否隊長；不含 Discord 身分（§9.12）。</summary>
public class TeamMemberDto
{
    public string? DiscordName { get; set; } // 隊友以「人」呈現；CharacterName 僅 DiscordName 空時 fallback
    public string? CharacterName { get; set; }
    public string? Job { get; set; }
    public int AttackPower { get; set; }        // 隊友戰力（成員可看）——攻擊快照
    public int MapleBlessingLevel { get; set; } // 隊友戰力——祝福等級
    public bool IsLeader { get; set; }
}

/// <summary>某隊 Confirmed 成員一列（隊長轉讓挑人用）：memberId 當轉讓目標（不外流 raw discordId）+ 顯示名（人的 DiscordName，隊長轉讓是換「人」；CharacterName 僅 DiscordName 空時 fallback）。</summary>
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
    /// <summary>已確認成員的能力（職業/攻擊/祝福等級，不含身分）——讓尋隊者看組成+戰力、判斷要不要申請。§9.12：公開面不露 Discord/角色名。</summary>
    public List<OpenTeamMemberDto> ConfirmedMembers { get; set; } = [];
}

/// <summary>尋隊看得到的一個已確認成員能力：只職業/攻擊/祝福，不含任何身分（§9.12 公開面）。</summary>
public class OpenTeamMemberDto
{
    public string? Job { get; set; }
    public int AttackPower { get; set; }
    public int MapleBlessingLevel { get; set; }
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

/// <summary>招募缺口一列（隊長挑候選時看「還缺什麼職業」）：一組可接受職業 + 還缺幾位。
/// 軟提示，不強制組成；已 Confirmed 成員以「職業」計入（進隊即填該格，不再看攻擊/通關門檻）。</summary>
public class RecruitmentGapRowDto
{
    public List<string> Jobs { get; set; } = []; // 空 = 不限職業
    public int Required { get; set; }
    public int Remaining { get; set; }
}
