using Application.Events;
using Infrastructure.BackgroundJobs;
using Xunit;

namespace Test;

/// <summary>事件 roster/角色快照 → 邀請/申請 DM embed 的映射（bot-composed-embeds）。</summary>
public class InviteEmbedTests
{
    private static TeamEmbedData Sample() => new()
    {
        BossName = "混沌王",
        TimeText = "8/26 20:00",
        Capacity = 6,
        SubjectName = "小明",
        SubjectJob = "夜光",
        SubjectAttackPower = 5200,
        SubjectMapleBlessingLevel = 95,
        Roster = new()
        {
            new RosterEntry { Job = "英雄", AttackPower = 5400, MapleBlessingLevel = 100 },
            new RosterEntry { Job = "夜使者", AttackPower = 4800, MapleBlessingLevel = 90 }
        }
    };

    [Fact]
    public void 邀請_標題王名時間_內文被邀角色與成員_頁尾缺額()
    {
        var e = TeamNotificationOutboxHandler.BuildActionEmbed(TeamNotificationAction.InviteResponse, Sample());

        Assert.Contains("混沌王", e.Title);
        Assert.Contains("8/26 20:00", e.Title);           // 標題＝王名＋時間
        Assert.Contains("被邀角色", e.Description);
        Assert.Contains("小明", e.Description);             // 被邀角色
        Assert.Contains("夜光", e.Description);
        Assert.DoesNotContain("隊長邀請你加入", e.Description);  // 多餘引言已移除
        Assert.Contains("英雄", e.Description);
        Assert.Contains("攻5400", e.Description);
        Assert.Contains("祝福100", e.Description);
        Assert.Equal("缺額 4／6", e.Footer);   // 6 - 2
    }

    [Fact]
    public void 申請_內文用申請角色標籤()
    {
        var e = TeamNotificationOutboxHandler.BuildActionEmbed(TeamNotificationAction.ApplicationReview, Sample());

        Assert.Contains("申請角色", e.Description);
        Assert.Contains("小明", e.Description);
        Assert.DoesNotContain("被邀角色", e.Description);
    }

    [Fact]
    public void 轉讓_無主角行_只列成員()
    {
        var e = TeamNotificationOutboxHandler.BuildActionEmbed(TeamNotificationAction.TransferResponse, Sample());

        Assert.Contains("混沌王", e.Title);
        Assert.Contains("轉讓", e.Description);          // 轉讓說明
        Assert.DoesNotContain("被邀角色", e.Description);
        Assert.DoesNotContain("申請角色", e.Description);
        Assert.DoesNotContain("小明", e.Description);   // 轉讓無主角
        Assert.Contains("英雄", e.Description);          // 但仍列目前成員
    }

    [Fact]
    public void 空隊_內文顯示尚無成員_缺額為滿容量()
    {
        var d = Sample();
        d.Roster = new();

        var e = TeamNotificationOutboxHandler.BuildActionEmbed(TeamNotificationAction.InviteResponse, d);

        Assert.Contains("尚無", e.Description);
        Assert.Equal("缺額 6／6", e.Footer);
    }

    [Fact]
    public void 已達容量_缺額不為負()
    {
        var d = Sample();
        d.Capacity = 1;

        var e = TeamNotificationOutboxHandler.BuildActionEmbed(TeamNotificationAction.InviteResponse, d);

        Assert.Equal("缺額 0／1", e.Footer);   // Math.Max(0, 1-2) = 0
    }
}
