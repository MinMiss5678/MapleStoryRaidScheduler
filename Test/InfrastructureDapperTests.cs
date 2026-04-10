using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Domain.Attributes;
using Infrastructure.Dapper;
using Moq;
using Xunit;

namespace Test;

// 測試用 entity (帶有 [Key])
[Table("TestEntity")]
public class DapperTestEntity
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// 測試用 entity (帶有 [ExplicitKey])
[Table("ExplicitTestEntity")]
public class ExplicitKeyTestEntity
{
    [ExplicitKey]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class InfrastructureDapperTests
{
    private Mock<IDbConnection> CreateMockConnection()
    {
        var mockConn = new Mock<IDbConnection>();
        return mockConn;
    }

    // ========== TimeOnlyTypeHandler ==========

    [Theory]
    [InlineData(10, 30, 10, 30)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(23, 59, 23, 59)]
    public void TimeOnlyTypeHandler_Parse_TimeSpan_ReturnsCorrectTimeOnly(int h, int m, int expectedH, int expectedM)
    {
        var handler = new TimeOnlyTypeHandler();
        var ts = new TimeSpan(h, m, 0);
        var result = handler.Parse(ts);
        Assert.Equal(new TimeOnly(expectedH, expectedM), result);
    }

    [Fact]
    public void TimeOnlyTypeHandler_Parse_DateTime_ReturnsCorrectTimeOnly()
    {
        var handler = new TimeOnlyTypeHandler();
        var dt = new DateTime(2024, 1, 1, 14, 30, 0);
        var result = handler.Parse(dt);
        Assert.Equal(new TimeOnly(14, 30), result);
    }

    [Fact]
    public void TimeOnlyTypeHandler_Parse_DateTimeOffset_ReturnsCorrectTimeOnly()
    {
        var handler = new TimeOnlyTypeHandler();
        var dto = new DateTimeOffset(2024, 1, 1, 9, 15, 0, TimeSpan.Zero);
        var result = handler.Parse(dto);
        Assert.Equal(new TimeOnly(9, 15), result);
    }

    [Fact]
    public void TimeOnlyTypeHandler_Parse_String_ReturnsCorrectTimeOnly()
    {
        var handler = new TimeOnlyTypeHandler();
        var result = handler.Parse("08:00:00");
        Assert.Equal(new TimeOnly(8, 0), result);
    }

    [Fact]
    public void TimeOnlyTypeHandler_SetValue_SetsParameterValue()
    {
        var handler = new TimeOnlyTypeHandler();
        var mockParam = new Mock<IDbDataParameter>();
        mockParam.SetupSet(p => p.Value = It.IsAny<object>()).Verifiable();

        handler.SetValue(mockParam.Object, new TimeOnly(12, 0));

        mockParam.VerifySet(p => p.Value = It.IsAny<object>(), Times.Once);
    }

    // ========== DbContext ==========

    [Fact]
    public void DbContext_Begin_StartsTransaction()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        ctx.Begin();

        mockConn.Verify(c => c.BeginTransaction(), Times.Once);
    }

    [Fact]
    public void DbContext_Begin_WhenTransactionAlreadyExists_DoesNotBeginAgain()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        ctx.Begin();
        ctx.Begin(); // 第二次應跳過

        mockConn.Verify(c => c.BeginTransaction(), Times.Once);
    }

    [Fact]
    public void DbContext_Commit_CommitsTransaction()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        ctx.Begin();
        ctx.Commit();

        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    [Fact]
    public void DbContext_Rollback_RollsBackTransaction()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        ctx.Begin();
        ctx.Rollback();

        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    [Fact]
    public void DbContext_Repository_ReturnsRepositoryInstance()
    {
        var mockConn = new Mock<IDbConnection>();
        var ctx = new DbContext(mockConn.Object);

        var repo = ctx.Repository<DapperTestEntity>();
        Assert.NotNull(repo);
    }

    // ========== UnitOfWork ==========

    [Fact]
    public async Task UnitOfWork_BeginAsync_CallsContextBegin()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        var uow = new UnitOfWork(ctx);

        await uow.BeginAsync();

        mockConn.Verify(c => c.BeginTransaction(), Times.Once);
    }

    [Fact]
    public async Task UnitOfWork_CommitAsync_CommitsTransaction()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        var uow = new UnitOfWork(ctx);

        await uow.BeginAsync();
        await uow.CommitAsync();

        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    [Fact]
    public async Task UnitOfWork_RollbackAsync_RollsBackTransaction()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        mockConn.Setup(c => c.BeginTransaction()).Returns(mockTx.Object);

        var ctx = new DbContext(mockConn.Object);
        var uow = new UnitOfWork(ctx);

        await uow.BeginAsync();
        await uow.RollbackAsync();

        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ========== DapperRepository ==========

    [Fact]
    public void DapperRepository_Constructor_WithKeyAttribute_InitializesCorrectly()
    {
        var mockConn = new Mock<IDbConnection>();
        var repo = new DapperRepository<DapperTestEntity>(mockConn.Object);
        Assert.NotNull(repo);
    }

    [Fact]
    public void DapperRepository_Constructor_WithExplicitKeyAttribute_InitializesCorrectly()
    {
        var mockConn = new Mock<IDbConnection>();
        var repo = new DapperRepository<ExplicitKeyTestEntity>(mockConn.Object);
        Assert.NotNull(repo);
    }

    [Fact]
    public void DapperRepository_Constructor_WithTransaction_InitializesCorrectly()
    {
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();
        var repo = new DapperRepository<DapperTestEntity>(mockConn.Object, mockTx.Object);
        Assert.NotNull(repo);
    }
}
