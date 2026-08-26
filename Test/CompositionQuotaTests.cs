using Domain.Entities;
using Domain.Helpers;
using Xunit;

namespace Test;

/// <summary>組隊職業名額可行性（composition-quota）：二分匹配 + 未指定池 + OR/重疊群組 + 容量溢出。</summary>
public class CompositionQuotaTests
{
    private static TeamSlotRequirement Req(int count, params string[] jobs) => new()
    {
        Count = count,
        Jobs = jobs.Select(j => new TeamSlotRequirementJob { Job = j, MinAttackPower = 0 }).ToList()
    };

    [Fact]
    public void 無需求_只受容量限制()
    {
        Assert.True(CompositionQuota.IsFeasible(["A", "B"], [], 6));
        Assert.False(CompositionQuota.IsFeasible(["A", "B", "C", "D", "E", "F"], [], 5)); // 6 > 5
    }

    [Fact]
    public void 單職業列_擋重複職業()
    {
        var reqs = new[] { Req(1, "黑騎士"), Req(1, "英雄") }; // 容量 2、恰滿
        Assert.True(CompositionQuota.IsFeasible(["黑騎士", "英雄"], reqs, 2));
        Assert.False(CompositionQuota.IsFeasible(["黑騎士", "黑騎士"], reqs, 2)); // 第 2 黑騎士無名額
    }

    [Fact]
    public void 未指定名額_容納任意職業()
    {
        var reqs = new[] { Req(1, "黑騎士") }; // 容量 6 → 1 指定 + 5 未指定
        Assert.True(CompositionQuota.IsFeasible(["黑騎士", "夜使者", "夜使者"], reqs, 6));
        Assert.True(CompositionQuota.IsFeasible(["夜使者"], reqs, 6)); // 黑騎士名額空著也可行（只問現有成員能否安置）
    }

    [Fact]
    public void Or群組_同群組只收一個()
    {
        var reqs = new[] { Req(1, "箭神", "槍神") }; // 容量 1
        Assert.True(CompositionQuota.IsFeasible(["箭神"], reqs, 1));
        Assert.False(CompositionQuota.IsFeasible(["箭神", "槍神"], reqs, 1)); // 群組只 1 位
    }

    [Fact]
    public void 重疊Or群組_由匹配化解()
    {
        var reqs = new[] { Req(1, "黑騎士", "英雄"), Req(1, "英雄", "主教") }; // 容量 2
        Assert.True(CompositionQuota.IsFeasible(["英雄", "英雄"], reqs, 2));   // 各配一列
        Assert.True(CompositionQuota.IsFeasible(["英雄", "主教"], reqs, 2));
        Assert.False(CompositionQuota.IsFeasible(["黑騎士", "黑騎士"], reqs, 2)); // 只一列收黑騎士
    }

    [Fact]
    public void 容量溢出_不可行()
    {
        var reqs = new[] { Req(1, "英雄") };
        Assert.False(CompositionQuota.IsFeasible(["英雄", "英雄"], reqs, 1)); // 2 成員 > 1 名額
    }

    // ── MaxRequirementSlotsFilled（招募熱力圖：候選供給 → 需求名額覆蓋）──
    [Fact]
    public void 填滿格數_全覆蓋與部分覆蓋()
    {
        var reqs = new[] { Req(1, "黑騎士"), Req(1, "英雄") }; // ΣCount=2
        Assert.Equal(2, CompositionQuota.MaxRequirementSlotsFilled(["黑騎士", "英雄"], reqs)); // 整套可填
        Assert.Equal(1, CompositionQuota.MaxRequirementSlotsFilled(["黑騎士", "黑騎士"], reqs)); // 只填黑騎士格
        Assert.Equal(0, CompositionQuota.MaxRequirementSlotsFilled(["主教"], reqs));           // 都不合
    }

    [Fact]
    public void 填滿格數_OR與重疊群組()
    {
        Assert.Equal(1, CompositionQuota.MaxRequirementSlotsFilled(["箭神", "槍神"], [Req(1, "箭神", "槍神")])); // 群組只 1 名額
        var overlap = new[] { Req(1, "黑騎士", "英雄"), Req(1, "英雄", "主教") };
        Assert.Equal(2, CompositionQuota.MaxRequirementSlotsFilled(["英雄", "英雄"], overlap)); // 各配一列
        Assert.Equal(1, CompositionQuota.MaxRequirementSlotsFilled(["英雄"], overlap));         // 一個只能填一格
    }

    [Fact]
    public void 填滿格數_供給過剩與無需求()
    {
        Assert.Equal(1, CompositionQuota.MaxRequirementSlotsFilled(["英雄", "英雄", "英雄"], [Req(1, "英雄")])); // 只 1 名額
        Assert.Equal(0, CompositionQuota.MaxRequirementSlotsFilled(["英雄"], []));                              // 無需求列 → 0 格
    }
}
