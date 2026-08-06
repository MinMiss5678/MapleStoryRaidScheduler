using System.Text.Json;
using Application.Events;
using Application.Interface;
using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Test;

public class TeamNotificationOutboxHandlerTests
{
    private static TeamNotificationOutboxHandler Handler(Mock<IDiscordService> discord) =>
        new(discord.Object, new Mock<ILogger<TeamNotificationOutboxHandler>>().Object);

    [Fact]
    public async Task HandleAsync_SendsDM_WithDeserializedTargetAndMessage()
    {
        var discord = new Mock<IDiscordService>();
        var payload = JsonSerializer.Serialize(new TeamNotificationEvent { TargetDiscordId = 555, Message = "你被邀請" });

        await Handler(discord).HandleAsync(payload, default);

        discord.Verify(d => d.SendDirectMessageAsync(555UL, "你被邀請"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Rethrows_OnTransientFailure_SoOutboxRetries()
    {
        // 暫時失敗（網路/限流）→ 應 rethrow 讓 outbox 重試（永久失敗〔關 DM/退公會〕才吞，見 handler）
        var discord = new Mock<IDiscordService>();
        discord.Setup(d => d.SendDirectMessageAsync(It.IsAny<ulong>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("network"));
        var payload = JsonSerializer.Serialize(new TeamNotificationEvent { TargetDiscordId = 1, Message = "x" });

        await Assert.ThrowsAsync<Exception>(() => Handler(discord).HandleAsync(payload, default));
    }
}
