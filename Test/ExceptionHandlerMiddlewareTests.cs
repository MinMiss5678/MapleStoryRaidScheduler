using System.Text.Json;
using Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Presentation.WebApi.Middleware;
using Xunit;

namespace Test;

/// <summary>
/// ExceptionHandlerMiddleware 單元測試：釘住
/// (1) 例外 → HTTP 狀態碼對映（客戶端契約）；
/// (2) 安全屬性——非開發環境隱藏 5xx 內部細節，4xx 一律顯示。
/// </summary>
public class ExceptionHandlerMiddlewareTests
{
    // 只有其他 AppException 子類會落到「AppException → 400」的 fallback（三個具體類已各自匹配）
    private sealed class OtherAppException(string message) : AppException(message);

    private static async Task<(int Status, ProblemDetails? Problem)> RunWith(Exception ex, string environment)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(environment);

        RequestDelegate next = _ => throw ex;
        var middleware = new ExceptionHandlerMiddleware(
            next, NullLogger<ExceptionHandlerMiddleware>.Instance, env.Object);

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.Invoke(context);

        body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return (context.Response.StatusCode, problem);
    }

    [Fact]
    public async Task NotFoundException_對映_404()
    {
        var (status, _) = await RunWith(new NotFoundException("找不到"), Environments.Production);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task BusinessException_對映_400()
    {
        var (status, _) = await RunWith(new BusinessException("違反規則"), Environments.Production);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task ForbiddenException_對映_403()
    {
        var (status, _) = await RunWith(new ForbiddenException("沒權限"), Environments.Production);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task 其他_AppException_子類_對映_400()
    {
        var (status, _) = await RunWith(new OtherAppException("其他業務錯"), Environments.Production);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task 未預期例外_對映_500()
    {
        var (status, _) = await RunWith(new InvalidOperationException("內部爆炸"), Environments.Production);
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }

    [Fact]
    public async Task 非開發環境_5xx_隱藏內部細節()
    {
        var (_, problem) = await RunWith(new InvalidOperationException("內部爆炸"), Environments.Production);
        Assert.Null(problem!.Detail); // 不外洩 500 的原始訊息
    }

    [Fact]
    public async Task 開發環境_5xx_顯示細節_便於除錯()
    {
        var (_, problem) = await RunWith(new InvalidOperationException("內部爆炸"), Environments.Development);
        Assert.Equal("內部爆炸", problem!.Detail);
    }

    [Fact]
    public async Task 非開發環境_4xx_仍顯示訊息_給前端提示()
    {
        var (_, problem) = await RunWith(new BusinessException("庫存不足"), Environments.Production);
        Assert.Equal("庫存不足", problem!.Detail); // 4xx 是可安全外露的業務訊息
    }

    [Fact]
    public async Task PostgresException_UniqueViolation_對映_409_且不外洩DB訊息()
    {
        // 並發 race 的 unique 違反是預期結果（非 bug）→ 409；且不回原始 DB 訊息（避免洩漏 constraint 名）
        var ex = new PostgresException("duplicate key value violates unique constraint \"pk_x\"",
            "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        var (status, problem) = await RunWith(ex, Environments.Production);
        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("發生併發衝突，請稍後重試。", problem!.Detail);
    }

    [Fact]
    public async Task PostgresException_其他約束違反_維持_500_且隱藏細節()
    {
        // FK/check/not-null/length 等應被 app 層先擋；到 DB 即 bug → 維持 500 + 告警 + 隱藏細節
        var ex = new PostgresException("insert violates foreign key constraint \"fk_x\"",
            "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);
        var (status, problem) = await RunWith(ex, Environments.Production);
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Null(problem!.Detail);
    }
}
