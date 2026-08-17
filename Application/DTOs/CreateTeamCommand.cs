using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

/// <summary>
/// 隊長開隊（leader-led Pull 的起點）：選王 + 時段 + 限定條件（Level 2）。
/// LeaderDiscordId 由 Controller 從登入身分注入，不信任 client（§5「不分權」，任何登入者可開隊）。
/// </summary>
public class CreateTeamCommand
{
    public ulong LeaderDiscordId { get; set; }

    [Range(1, int.MaxValue)]
    public int BossId { get; set; }

    public DateTimeOffset SlotDateTime { get; set; }

    /// <summary>隊伍種類（period-less §8）：Scheduled=排程 / Instant=即時。預設 Scheduled。</summary>
    public string Kind { get; set; } = "Scheduled";

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>隊長自己要帶下去打的角色（佔 1 位、自動 Confirmed）；null = 只揪人、自己不打。須為本人角色。</summary>
    public string? LeaderCharacterId { get; set; }

    public List<CreateTeamRequirementDto> Requirements { get; set; } = [];
}

/// <summary>一個需求列＝一組可接受職業（各帶攻擊下限）+ 數量 + 通關數門檻。</summary>
public class CreateTeamRequirementDto
{
    [Range(1, int.MaxValue)]
    public int Count { get; set; } = 1;

    [Range(0, int.MaxValue)]
    public int MinClearCount { get; set; }

    public List<CreateTeamRequirementJobDto> Jobs { get; set; } = [];
}

public class CreateTeamRequirementJobDto
{
    [Required]
    public required string Job { get; set; }

    [Range(0, int.MaxValue)]
    public int MinAttackPower { get; set; }
}
