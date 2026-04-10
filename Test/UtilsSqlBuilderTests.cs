using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Dapper;
using Utils.SqlBuilder;
using Xunit;

namespace Test;

// 測試用的 DB Model
[Table("Character")]
public class TestCharacter
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Job { get; set; } = "";
}

[Table("Boss")]
public class TestBoss
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int RequireMembers { get; set; }
}

public class UtilsSqlBuilderTests
{
    // ========== CteBuilder ==========

    [Fact]
    public void CteBuilder_NoCtes_ReturnsMainSql()
    {
        var builder = new CteBuilder();
        var result = builder.Build("SELECT 1");
        Assert.Equal("SELECT 1", result);
    }

    [Fact]
    public void CteBuilder_WithOneCte_PrefixesWithWith()
    {
        var result = new CteBuilder()
            .With("cte1", "SELECT Id FROM \"Character\"")
            .Build("SELECT * FROM cte1");

        Assert.StartsWith("WITH cte1 AS (SELECT Id FROM \"Character\")", result);
    }

    [Fact]
    public void CteBuilder_WithMultipleCtes_JoinsWithComma()
    {
        var result = new CteBuilder()
            .With("a", "SELECT 1")
            .With("b", "SELECT 2")
            .Build("SELECT * FROM a, b");

        Assert.Contains("a AS (SELECT 1), b AS (SELECT 2)", result);
    }

    // ========== QueryBuilder ==========

    [Fact]
    public void QueryBuilder_BasicFromSelect_BuildsCorrectSql()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id, x.Name });

        var (sql, _) = qb.Build();
        Assert.Contains("FROM \"Character\" AS a", sql);
        Assert.Contains("a.\"Id\"", sql);
        Assert.Contains("a.\"Name\"", sql);
    }

    [Fact]
    public void QueryBuilder_WithWhere_AddsWhereClause()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .Where<TestCharacter>(x => x.Id == 1);

        var (sql, param) = qb.Build();
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void QueryBuilder_WithOrWhere_AddsOrCondition()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .Where<TestCharacter>(x => x.Age > 10)
          .OrWhere<TestCharacter>(x => x.Age < 5);

        var (sql, _) = qb.Build();
        Assert.Contains("WHERE", sql);
        Assert.Contains(" OR ", sql);
    }

    [Fact]
    public void QueryBuilder_WithWhereGroup_AddsGroupedCondition()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .WhereGroup(g => g.Where<TestCharacter>(x => x.Id > 0));

        var (sql, _) = qb.Build();
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void QueryBuilder_WithOrWhereGroup_AddsOrGroupedCondition()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .OrWhereGroup(g => g.Where<TestCharacter>(x => x.Id > 0));

        var (sql, _) = qb.Build();
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void QueryBuilder_WithLeftJoin_AddsJoinClause()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .LeftJoin<TestBoss>(@"a.""Id"" = b.""Id""");

        var (sql, _) = qb.Build();
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("\"Boss\"", sql);
    }

    [Fact]
    public void QueryBuilder_WithOrderByDescending_AddsOrderByClause()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .OrderByDescending<TestCharacter>(x => x.Id);

        var (sql, _) = qb.Build();
        Assert.Contains("ORDER BY \"Id\" DESC", sql);
    }

    [Fact]
    public void QueryBuilder_WithOrderByDescendingCastExpression_AddsOrderByClause()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .OrderByDescending<TestCharacter>(x => (object)x.Age);

        var (sql, _) = qb.Build();
        Assert.Contains("ORDER BY \"Age\" DESC", sql);
    }

    [Fact]
    public void QueryBuilder_WithLimit_AddsLimitClause()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .Limit(10);

        var (sql, _) = qb.Build();
        Assert.Contains("LIMIT 10", sql);
    }

    [Fact]
    public void QueryBuilder_WithOffset_AddsOffsetClause()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .Offset(5);

        var (sql, _) = qb.Build();
        Assert.Contains("OFFSET 5", sql);
    }

    [Fact]
    public void QueryBuilder_SelectSingleStringMember_BuildsCorrectColumnSql()
    {
        // string 屬性不需要 boxing，Body 直接為 MemberExpression
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => x.Name);

        var (sql, _) = qb.Build();
        Assert.Contains("a.\"Name\"", sql);
    }

    [Fact]
    public void QueryBuilder_NoSelects_UsesWildcard()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>();

        var (sql, _) = qb.Build();
        Assert.Contains("SELECT *", sql);
    }

    // ========== InsertBuilder ==========

    [Fact]
    public void InsertBuilder_BasicInsert_BuildsCorrectSql()
    {
        var (sql, param) = new InsertBuilder<TestCharacter>()
            .Set(x => x.Name, "Hero")
            .Set(x => x.Age, 20)
            .Build();

        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("\"Character\"", sql);
        Assert.Contains("\"Name\"", sql);
        Assert.Contains("\"Age\"", sql);
    }

    [Fact]
    public void InsertBuilder_WithReturnId_AddsReturningClause()
    {
        var (sql, _) = new InsertBuilder<TestCharacter>()
            .Set(x => x.Name, "Hero")
            .ReturnId()
            .Build();

        Assert.Contains("RETURNING \"Id\"", sql);
    }

    [Fact]
    public void InsertBuilder_WithoutReturnId_NoReturningClause()
    {
        var (sql, _) = new InsertBuilder<TestCharacter>()
            .Set(x => x.Name, "Hero")
            .Build();

        Assert.DoesNotContain("RETURNING", sql);
    }

    // ========== UpdateBuilder ==========

    [Fact]
    public void UpdateBuilder_BasicUpdate_BuildsCorrectSql()
    {
        var (sql, _) = new UpdateBuilder<TestCharacter>()
            .Set(x => x.Name, "NewName")
            .Where(x => x.Id == 1)
            .Build();

        Assert.Contains("UPDATE", sql);
        Assert.Contains("\"Name\"", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void UpdateBuilder_WithoutWhere_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new UpdateBuilder<TestCharacter>()
                .Set(x => x.Name, "Test")
                .Build());
    }

    [Fact]
    public void UpdateBuilder_NoSetColumns_ReturnsNoOpSql()
    {
        var (sql, _) = new UpdateBuilder<TestCharacter>()
            .Where(x => x.Id == 1)
            .Build();

        Assert.Equal("SELECT 0", sql);
    }

    // ========== DeleteBuilder ==========

    [Fact]
    public void DeleteBuilder_BasicDelete_BuildsCorrectSql()
    {
        var (sql, _) = new DeleteBuilder<TestCharacter>()
            .Where(x => x.Id == 1)
            .Build();

        Assert.Contains("DELETE FROM", sql);
        Assert.Contains("\"Character\"", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void DeleteBuilder_WithOrWhere_AddsOrCondition()
    {
        var (sql, _) = new DeleteBuilder<TestCharacter>()
            .Where(x => x.Age > 5)
            .OrWhere(x => x.Age < 2)
            .Build();

        Assert.Contains("OR", sql);
    }

    [Fact]
    public void DeleteBuilder_WithWhereGroup_AddsGroupCondition()
    {
        var (sql, _) = new DeleteBuilder<TestCharacter>()
            .WhereGroup(g => g.Where(x => x.Id > 0))
            .Build();

        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void DeleteBuilder_WithWhereRaw_AddsRawCondition()
    {
        var (sql, _) = new DeleteBuilder<TestCharacter>()
            .WhereRaw("\"Id\" IS NOT NULL")
            .Build();

        Assert.Contains("\"Id\" IS NOT NULL", sql);
    }

    [Fact]
    public void DeleteBuilder_WithoutWhere_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DeleteBuilder<TestCharacter>().Build());
    }

    // ========== SqlExpressionVisitor ==========

    [Fact]
    public void SqlExpressionVisitor_EqualExpression_BuildsEqualSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id == 5)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" = ", sql);
        Assert.Contains("a.\"Id\"", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_NotEqualExpression_BuildsNotEqualSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id != 5)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" <> ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_GreaterThanExpression_BuildsGtSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Age > 10)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" > ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_LessThanExpression_BuildsLtSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Age < 10)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" < ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_GreaterThanOrEqualExpression_BuildsGteSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Age >= 10)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" >= ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_LessThanOrEqualExpression_BuildsLteSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Age <= 10)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" <= ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_AndAlsoExpression_BuildsAndSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id > 0 && x.Age > 0)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" AND ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_OrElseExpression_BuildsOrSql()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id == 1 || x.Id == 2)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains(" OR ", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_ConstantExpression_AddsParameter()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        // 常數比較
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id == 99)).Body;
        var sql = visitor.Translate(expr);
        Assert.NotEmpty(sql);
    }

    [Fact]
    public void SqlExpressionVisitor_ContainsExpression_BuildsAnySql()
    {
        var ids = new List<int> { 1, 2, 3 };
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => ids.Contains(x.Id))).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains("ANY(", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_ContainsEmptyList_BuildsAlwaysFalseSql()
    {
        var ids = new List<int>();
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => ids.Contains(x.Id))).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains("1 = 0", sql);
    }

    [Fact]
    public void SqlExpressionVisitor_MemberAccessOnCapturedVariable_AddsParameter()
    {
        var charObj = new TestCharacter { Age = 25 };
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Age == charObj.Age)).Body;
        var sql = visitor.Translate(expr);
        Assert.NotEmpty(sql);
    }

    [Fact]
    public void SqlExpressionVisitor_ConvertExpression_HandlesCorrectly()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor("a", params_);
        // ulong cast causes Convert expression
        long id = 5L;
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id == (int)id)).Body;
        var sql = visitor.Translate(expr);
        Assert.NotEmpty(sql);
    }

    [Fact]
    public void SqlExpressionVisitor_NoAlias_FormatsColumnWithoutAlias()
    {
        var params_ = new DynamicParameters();
        var visitor = new SqlExpressionVisitor(null, params_);
        var expr = ((System.Linq.Expressions.Expression<Func<TestCharacter, bool>>)(x => x.Id == 1)).Body;
        var sql = visitor.Translate(expr);
        Assert.Contains("\"Id\"", sql);
        Assert.DoesNotContain("a.\"Id\"", sql);
    }

    // ========== SqlConditionGroup (via QueryBuilder) ==========

    [Fact]
    public void QueryBuilder_MultipleAndConditions_GeneratesCorrectAndSql()
    {
        // 測試 SqlConditionGroup 的多條件 AND 串接（透過 QueryBuilder）
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .Where<TestCharacter>(x => x.Id > 0)
          .Where<TestCharacter>(x => x.Age < 100);

        var (sql, _) = qb.Build();
        Assert.Contains(" AND ", sql);
    }

    [Fact]
    public void QueryBuilder_OrConditions_GeneratesCorrectOrSql()
    {
        // 測試 SqlConditionGroup OR 條件
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .Where<TestCharacter>(x => x.Id == 1)
          .OrWhere<TestCharacter>(x => x.Id == 2);

        var (sql, _) = qb.Build();
        Assert.Contains(" OR ", sql);
    }

    [Fact]
    public void QueryBuilder_NestedWhereGroups_GeneratesGroupedConditions()
    {
        // 測試 SqlConditionGroup 巢狀包括括號
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .WhereGroup(g =>
          {
              g.Where<TestCharacter>(x => x.Id == 1);
              g.OrWhere<TestCharacter>(x => x.Id == 2);
          })
          .Where<TestCharacter>(x => x.Age > 0);

        var (sql, _) = qb.Build();
        // 巢狀 group 應產生括號
        Assert.Contains("(", sql);
    }

    [Fact]
    public void QueryBuilder_OrNestedGroup_GeneratesOrGroupedConditions()
    {
        var qb = new QueryBuilder();
        qb.From<TestCharacter>()
          .Select<TestCharacter>(x => new { x.Id })
          .OrWhereGroup(g => g.Where<TestCharacter>(x => x.Id > 0));

        var (sql, _) = qb.Build();
        Assert.Contains("WHERE", sql);
    }

    // ========== Result<T> ==========

    [Fact]
    public void Result_Success_IsSuccessTrue()
    {
        var result = Result<int>.Success(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Data);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Result_Fail_IsSuccessFalse()
    {
        var result = Result<int>.Fail("error occurred");
        Assert.False(result.IsSuccess);
        Assert.Equal("error occurred", result.ErrorMessage);
    }

    // ========== DbExecutor ==========

    [Fact]
    public async Task DbExecutor_SuccessfulFunc_ReturnsSuccess()
    {
        var executor = new DbExecutor();
        var result = await executor.ExecuteAsync(async () => { await Task.CompletedTask; return 42; });
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Data);
    }

    [Fact]
    public async Task DbExecutor_ThrowingFunc_ReturnsFailure()
    {
        var executor = new DbExecutor();
        var result = await executor.ExecuteAsync<int>(() => throw new Exception("db error"));
        Assert.False(result.IsSuccess);
        Assert.Contains("db error", result.ErrorMessage);
    }
}
