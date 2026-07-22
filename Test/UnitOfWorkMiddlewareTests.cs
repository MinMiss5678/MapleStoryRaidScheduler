using Application.Interface;
using Microsoft.AspNetCore.Http;
using Moq;
using Presentation.WebApi.Middleware;
using Xunit;

namespace Test;

/// <summary>
/// UnitOfWorkMiddleware 單元測試：釘住交易邊界契約——
/// 非寫入不開交易；寫入成功(status &lt; 400) commit、失敗(&gt;= 400)或例外 rollback；例外要往外拋。
/// mock IUnitOfWork 驗 Begin/Commit/Rollback 的呼叫，不碰真 DB。
/// </summary>
public class UnitOfWorkMiddlewareTests
{
    private readonly Mock<IUnitOfWork> _uow = new();

    // next 可自訂：設定回應狀態碼或丟例外
    private static UnitOfWorkMiddleware BuildMiddleware(RequestDelegate next) => new(next);

    private static DefaultHttpContext BuildContext(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        return context;
    }

    [Fact]
    public async Task 非寫入請求_不開交易_直接放行()
    {
        var nextCalled = false;
        var middleware = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.Invoke(BuildContext("GET"), _uow.Object);

        Assert.True(nextCalled);
        _uow.Verify(u => u.BeginAsync(), Times.Never);
        _uow.Verify(u => u.CommitAsync(), Times.Never);
        _uow.Verify(u => u.RollbackAsync(), Times.Never);
    }

    [Fact]
    public async Task 標記_SkipUow_的寫入請求_不開交易()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask);
        var context = BuildContext("POST");
        context.Items["SkipUow"] = true;

        await middleware.Invoke(context, _uow.Object);

        _uow.Verify(u => u.BeginAsync(), Times.Never);
    }

    [Fact]
    public async Task 寫入成功_開交易後_commit()
    {
        // next 不動狀態碼 → 預設 200 → 視為成功
        var middleware = BuildMiddleware(_ => Task.CompletedTask);

        await middleware.Invoke(BuildContext("POST"), _uow.Object);

        _uow.Verify(u => u.BeginAsync(), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Once);
        _uow.Verify(u => u.RollbackAsync(), Times.Never);
    }

    [Fact]
    public async Task 寫入回_4xx_開交易後_rollback_不_commit()
    {
        var middleware = BuildMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        await middleware.Invoke(BuildContext("PUT"), _uow.Object);

        _uow.Verify(u => u.BeginAsync(), Times.Once);
        _uow.Verify(u => u.RollbackAsync(), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task 寫入丟例外_rollback_並往外拋()
    {
        var boom = new InvalidOperationException("boom");
        var middleware = BuildMiddleware(_ => throw boom);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.Invoke(BuildContext("DELETE"), _uow.Object));

        Assert.Same(boom, thrown);
        _uow.Verify(u => u.BeginAsync(), Times.Once);
        _uow.Verify(u => u.RollbackAsync(), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Never);
    }
}
