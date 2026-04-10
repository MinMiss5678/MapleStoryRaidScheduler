using System.Data;
using System.Linq.Expressions;
using Application.Options;
using Application.Queries;
using Dapper;
using Infrastructure.Dapper;
using Infrastructure.Query;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Moq;
using Utils.SqlBuilder;
using Xunit;

namespace Test;

public class AdditionalCoverageTests
{
    // ========== SqlCommandBuilder base class Where ==========

    [Fact]
    public void SqlCommandBuilder_Where_ThrowsNotImplementedException()
    {
        // 呼叫 InsertBuilder 繼承自 SqlCommandBuilder 的 Where 方法
        // (InsertBuilder 本身沒有覆寫 Where，故觸發基底類別)
        var builder = new InsertBuilder<TestCharacter>();

        // 基底類 Where 呼叫 ExpressionToSql 會丟出 NotImplementedException
        Assert.Throws<NotImplementedException>(() =>
            builder.Where(x => x.Id == 1));
    }

    // ========== QueryBuilder unsupported expression ==========

    [Fact]
    public void QueryBuilder_OrderByDescending_UnsupportedExpression_ThrowsNotSupportedException()
    {
        // 只有 MemberExpression 和 UnaryExpression 有支援，其他會拋例外
        var qb = new QueryBuilder();
        qb.From<TestCharacter>();
        Assert.Throws<NotSupportedException>(() =>
            qb.OrderByDescending<TestCharacter>(x => (object)(x.Id + 1)));
    }

    // ========== SqlExpressionVisitor missing cases ==========

    [Fact]
    public void SqlExpressionVisitor_PropertyAccessOnCapturedObject_BuildsCorrectSql()
    {
        // VisitMember with PropertyInfo branch
        var target = new TestCharacter { Id = 10 };
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        // x => x.Id == target.Id 會讓 target.Id 進入 PropertyInfo 分支
        Expression<Func<TestCharacter, bool>> expr = x => x.Id == target.Id;
        var sql = visitor.Translate(expr.Body);
        Assert.Contains(" = ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_StaticEnumerableContains_BuildsAnySql()
    {
        // 測試 LINQ Enumerable.Contains 靜態方法呼叫
        var ids = new[] { 1, 2, 3 };
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        Expression<Func<TestCharacter, bool>> expr = x => ids.Contains(x.Id);
        var sql = visitor.Translate(expr.Body);
        Assert.Contains("ANY(", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_UnsupportedBinaryOperator_ThrowsNotSupportedException()
    {
        // 不支援的二元運算子 (例如 Add)
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        // x.Id + 1 會是 Add 運算子，不在 switch 中
        Expression<Func<TestCharacter, int>> rawExpr = x => x.Id + 1;
        // 我們需要手動建立一個不支援的 BinaryExpression
        var param = Expression.Parameter(typeof(TestCharacter), "x");
        var left = Expression.Property(param, "Id");
        var right = Expression.Constant(1);
        var addExpr = Expression.Add(left, right);
        Assert.Throws<NotSupportedException>(() => visitor.Translate(addExpr));
    }

    [Fact]
    public void SqlExpressionVisitor_ConvertConstantExpression_HandlesBoxing()
    {
        // 測試 VisitUnary 中 ConstantExpression 在 Convert 中的分支
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        // ulong cast to long
        ulong discordId = 123456789UL;
        Expression<Func<TestCharacter, bool>> expr = x => x.Id == (int)(long)discordId;
        var sql = visitor.Translate(expr.Body);
        Assert.Contains("a.\"Id\"", sql);
    }

    // ========== BigIntStringConverter missing cases ==========

    [Fact]
    public void BigIntStringConverter_WriteJson_LongNullable_WritesValue()
    {
        var converter = new Utils.JsonConverters.BigIntStringConverter();
        using var sw = new System.IO.StringWriter();
        using var writer = new Newtonsoft.Json.JsonTextWriter(sw);
        converter.WriteJson(writer, (long?)12345L, null!);
        Assert.Equal("\"12345\"", sw.ToString());
    }

    // ========== DbContext EnsureNotCompleted ==========

    [Fact]
    public async Task DbContext_ExecuteAfterCommit_ThrowsInvalidOperationException()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        ctx.Begin();
        ctx.Commit();

        // 提交後再呼叫 ExecuteAsync 應該拋例外（EnsureNotCompleted）
        var builder = new InsertBuilder<TestCharacter>()
            .Set(x => x.Name, "test");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.ExecuteAsync(builder));
    }

    // ========== PlayerRegisterDbRow record ==========

    [Fact]
    public void PlayerRegisterDbRow_Constructor_StoresAllValues()
    {
        var row = new PlayerRegisterDbRow(
            Id: 1,
            DiscordId: 123456L,
            DiscordName: "TestUser",
            CharacterId: "char1",
            CharacterName: "Hero",
            Job: "Warrior",
            AttackPower: 5000,
            Rounds: 3,
            Weekday: 4,
            StartTime: new TimeOnly(20, 0),
            EndTime: new TimeOnly(22, 0));

        Assert.Equal(1, row.Id);
        Assert.Equal(123456L, row.DiscordId);
        Assert.Equal("TestUser", row.DiscordName);
        Assert.Equal("char1", row.CharacterId);
        Assert.Equal("Hero", row.CharacterName);
        Assert.Equal("Warrior", row.Job);
        Assert.Equal(5000, row.AttackPower);
        Assert.Equal(3, row.Rounds);
        Assert.Equal(4, row.Weekday);
        Assert.Equal(new TimeOnly(20, 0), row.StartTime);
        Assert.Equal(new TimeOnly(22, 0), row.EndTime);
    }

    // ========== DiscordOAuthClient ==========

    [Fact]
    public void DiscordOAuthClient_Constructor_InitializesCorrectly()
    {
        var optionsMock = new Mock<IOptions<DiscordOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new DiscordOptions
        {
            ClientId = "client-id",
            ClientSecret = "secret",
            RedirectUri = "http://localhost/callback",
            BotToken = "token",
            GuildId = "guild",
            ChannelId = "channel"
        });
        var httpClient = new System.Net.Http.HttpClient();

        var client = new DiscordOAuthClient(optionsMock.Object, httpClient);
        Assert.NotNull(client);
    }
}
