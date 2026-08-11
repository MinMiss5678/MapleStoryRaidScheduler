using Application.DTOs;
using Domain.Entities;

namespace Application.Queries;

/// <summary>
/// 候選池查詢（leader-led §4）。撈某週期報名池裡的角色 + 其時段 + 本王總通關 + 楓葉祝福，
/// **boss-agnostic**（bossId 只用來算通關數，不拿來過濾候選——隊長選王、玩家只提供角色+時段）。
/// 時段重疊 / 需求比對由 service 用 <see cref="Domain.Helpers.SlotDateCalculator.IsTimeInAvailability"/> 過濾。
/// </summary>
public interface ITeamCandidateQuery
{
    Task<IEnumerable<CandidatePoolItem>> GetPoolAsync(int bossId);

    /// <summary>即時團候選池（period-less §8 Phase 3）：未過期的 LfgIntent（BossId 為 null 或 = 該王），非常設時段。</summary>
    Task<IEnumerable<CandidatePoolItem>> GetInstantPoolAsync(int bossId);

    /// <summary>某日期全部玩家的可用時段 override（period-less §8 Phase 2b）——供候選比對疊在常設上。</summary>
    Task<IEnumerable<AvailabilityOverrideItem>> GetOverridesForDateAsync(DateOnly date);

    /// <summary>
    /// 候選「退團率偏高」的 DiscordId 集合（Feature 1b）：窗內（SlotDateTime ≥ windowStart）以 DiscordId 聚合，
    /// 參加(Confirmed+Left) ≥ minSample 且 退團(Left)/參加 ≥ thresholdPercent% 者。給定 discordIds 範圍內計算。
    /// </summary>
    Task<IReadOnlyCollection<ulong>> GetHighLeaveRateDiscordIdsAsync(
        IEnumerable<ulong> discordIds, DateTimeOffset windowStart, int minSample, int thresholdPercent);
}

/// <summary>候選池的一筆角色（含其玩家的時段清單與本王總通關）。DiscordId 僅供 service 內部，不外流到 DTO。</summary>
public class CandidatePoolItem
{
    public required string CharacterId { get; set; }
    public required string CharacterName { get; set; }
    public ulong DiscordId { get; set; }          // service 內部去重用（不外流到 DTO）
    public string? DiscordName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int MapleBlessingLevel { get; set; }
    public int BossClearCount { get; set; }
    public List<PlayerAvailability> Availabilities { get; set; } = [];
}
