using Domain.Entities;
using Domain.Exceptions;
using Xunit;

namespace Test;

// Register 聚合的「每週場次預算」不變式（純 domain，不需 mock）。
public class RegisterBudgetTests
{
    private static Register Reg(params (string charId, int bossId, int rounds)[] items) => new()
    {
        CharacterRegisters = items
            .Select(i => new CharacterRegister { CharacterId = i.charId, BossId = i.bossId, Rounds = i.rounds })
            .ToList()
    };

    [Fact]
    public void WithinBudget_ShouldNotThrow()
    {
        // 消耗 1：3+4+7 = 14，剛好等於上限
        var reg = Reg(("hero", 1, 3), ("hero", 2, 4), ("hero", 3, 7));
        reg.EnsureRoundsWithinBudget(new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1 });
    }

    [Fact]
    public void ExceedsBudget_ShouldThrow()
    {
        var reg = Reg(("hero", 1, 8), ("hero", 2, 7)); // 15 > 14
        Assert.Throws<DomainException>(() =>
            reg.EnsureRoundsWithinBudget(new Dictionary<int, int> { [1] = 1, [2] = 1 }));
    }

    [Fact]
    public void ConsumptionWeighted_ShouldCountRoundsTimesConsumption()
    {
        // Boss 1 消耗 2：7 場 = 14（剛好）；8 場 = 16（超過）
        var ok = Reg(("hero", 1, 7));
        ok.EnsureRoundsWithinBudget(new Dictionary<int, int> { [1] = 2 });

        var over = Reg(("hero", 1, 8));
        Assert.Throws<DomainException>(() =>
            over.EnsureRoundsWithinBudget(new Dictionary<int, int> { [1] = 2 }));
    }

    [Fact]
    public void BudgetIsPerCharacter_NotCombined()
    {
        // 兩隻角色各 14，合計 28，但預算是「每隻角色」故不該 throw
        var reg = Reg(("heroA", 1, 14), ("heroB", 1, 14));
        reg.EnsureRoundsWithinBudget(new Dictionary<int, int> { [1] = 1 });
    }

    [Fact]
    public void UnknownBossId_DefaultsToConsumptionOne()
    {
        // 字典查不到的 BossId 以消耗 1 計：14 場 → 14，不超過
        var reg = Reg(("hero", 99, 14));
        reg.EnsureRoundsWithinBudget(new Dictionary<int, int>());
    }
}
