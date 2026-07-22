using System.Text.Json;
using Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
}
