using Domain.Exceptions;

namespace Domain.Entities;

public class Register
{
    /// <summary>每隻角色每週的場次預算上限（消耗加權後的總和不得超過此值）。後端單一事實來源。</summary>
    public const int MaxRoundsPerCharacter = 14;

    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public int PeriodId { get; set; }
    public List<CharacterRegister> CharacterRegisters { get; set; } = [];
    public List<PlayerAvailability> Availabilities { get; set; } = [];

    /// <summary>
    /// 不變式：每隻角色 Σ(場次 × 該 Boss 的 RoundConsumption) ≤ <see cref="MaxRoundsPerCharacter"/>。
    /// 消耗值由呼叫端（RegisterService）載入 Boss 後注入——domain 不碰 repository、保持純粹，
    /// 與 TeamSlot.Capacity（= Boss.RequireMembers）由 service 注入是同一模式。找不到的 BossId 以消耗 1 計。
    /// </summary>
    public void EnsureRoundsWithinBudget(IReadOnlyDictionary<int, int> roundConsumptionByBossId)
    {
        foreach (var perCharacter in CharacterRegisters.GroupBy(c => c.CharacterId))
        {
            var totalConsumption = perCharacter.Sum(c =>
                c.Rounds * (roundConsumptionByBossId.TryGetValue(c.BossId, out var consumption) ? consumption : 1));

            if (totalConsumption > MaxRoundsPerCharacter)
                throw new DomainException(
                    $"角色 {perCharacter.Key} 的場次總消耗（{totalConsumption}）超過每週上限（{MaxRoundsPerCharacter}）。");
        }
    }
}
