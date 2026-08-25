using Infrastructure.Discord;
using Xunit;

namespace Test;

public class TeamActionButtonTests
{
    [Theory]
    [InlineData(TeamActionFamily.Invite, true, 5, "inv:accept:5")]
    [InlineData(TeamActionFamily.Invite, false, 5, "inv:decline:5")]
    [InlineData(TeamActionFamily.Application, true, 7, "app:approve:7")]
    [InlineData(TeamActionFamily.Application, false, 7, "app:reject:7")]
    [InlineData(TeamActionFamily.Transfer, true, 9, "xfer:accept:9")]
    [InlineData(TeamActionFamily.Transfer, false, 9, "xfer:decline:9")]
    public void CustomId_格式正確(TeamActionFamily family, bool positive, int id, string expected)
    {
        Assert.Equal(expected, TeamActionButton.CustomId(family, positive, id));
    }

    [Theory]
    [InlineData("inv:accept:5", TeamActionFamily.Invite, true, 5)]
    [InlineData("inv:decline:5", TeamActionFamily.Invite, false, 5)]
    [InlineData("app:approve:7", TeamActionFamily.Application, true, 7)]
    [InlineData("app:reject:7", TeamActionFamily.Application, false, 7)]
    [InlineData("xfer:accept:9", TeamActionFamily.Transfer, true, 9)]
    [InlineData("xfer:decline:9", TeamActionFamily.Transfer, false, 9)]
    public void TryParse_合法(string customId, TeamActionFamily expFamily, bool expPositive, int expId)
    {
        Assert.True(TeamActionButton.TryParse(customId, out var family, out var positive, out var id));
        Assert.Equal(expFamily, family);
        Assert.Equal(expPositive, positive);
        Assert.Equal(expId, id);
    }

    [Theory]
    [InlineData("")]                  // 空
    [InlineData("foo:accept:1")]      // 前綴不符
    [InlineData("inv:approve:1")]     // 邀請族不認 approve
    [InlineData("app:accept:1")]      // 申請族不認 accept
    [InlineData("inv:accept")]        // 欄位不足
    [InlineData("inv:accept:x")]      // id 非數字
    [InlineData("inv:accept:1:2")]    // 欄位過多
    public void TryParse_非法_回false(string customId)
    {
        Assert.False(TeamActionButton.TryParse(customId, out _, out _, out _));
    }

    [Theory]
    [InlineData(TeamActionFamily.Invite, true, 111)]
    [InlineData(TeamActionFamily.Application, false, 222)]
    [InlineData(TeamActionFamily.Transfer, true, 333)]
    public void CustomId_可被TryParse_還原(TeamActionFamily family, bool positive, int id)
    {
        var cid = TeamActionButton.CustomId(family, positive, id);
        Assert.True(TeamActionButton.TryParse(cid, out var f, out var p, out var gotId));
        Assert.Equal(family, f);
        Assert.Equal(positive, p);
        Assert.Equal(id, gotId);
    }
}
