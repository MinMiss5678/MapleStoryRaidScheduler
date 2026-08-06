using Domain.Entities;

namespace Application.Queries;

/// <summary>
/// 候選池查詢（leader-led §4）。撈某週期報名池裡的角色 + 其時段 + 本王總通關 + 楓葉祝福，
/// **boss-agnostic**（bossId 只用來算通關數，不拿來過濾候選——隊長選王、玩家只提供角色+時段）。
/// 時段重疊 / 需求比對由 service 用 <see cref="Domain.Helpers.SlotDateCalculator.IsTimeInAvailability"/> 過濾。
/// </summary>
public interface ITeamCandidateQuery
{
    Task<IEnumerable<CandidatePoolItem>> GetPoolAsync(int periodId, int bossId);
}

/// <summary>候選池的一筆角色（含其玩家的時段清單與本王總通關）。DiscordId 僅供 service 內部，不外流到 DTO。</summary>
public class CandidatePoolItem
{
    public required string CharacterId { get; set; }
    public required string CharacterName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int MapleBlessingLevel { get; set; }
    public int BossClearCount { get; set; }
    public List<PlayerAvailability> Availabilities { get; set; } = [];
}
