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
        new(discord.Object, new Mock<IDbConnectionFactory>().Object, new Mock<ILogger<TeamNotificationOutboxHandler>>().Object);

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

    [Fact]
    public async Task HandleAsync_EditsDM_OnInviteRevokedCleanup()
    {
        // dm-revoke-cleanup：撤邀清理事件 → 編輯原 DM（不送新訊息）。
        var discord = new Mock<IDiscordService>();
        var payload = JsonSerializer.Serialize(new TeamNotificationEvent
        {
            TargetDiscordId = 555,
            Message = "此邀請已失效（隊伍已滿）。",
            Action = TeamNotificationAction.InviteRevokedCleanup,
            EditMessageId = 42
        });

        await Handler(discord).HandleAsync(payload, default);

        discord.Verify(d => d.EditDirectMessageAsync(555UL, 42UL, "此邀請已失效（隊伍已滿）。"), Times.Once);
        discord.Verify(d => d.SendDirectMessageAsync(It.IsAny<ulong>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SkipsEdit_WhenEditMessageIdNull()
    {
        // id 未回寫（DM 未派發就被撤）→ 跳過清理、不呼叫編輯。
        var discord = new Mock<IDiscordService>();
        var payload = JsonSerializer.Serialize(new TeamNotificationEvent
        {
            TargetDiscordId = 555,
            Message = "x",
            Action = TeamNotificationAction.InviteRevokedCleanup,
            EditMessageId = null
        });

        await Handler(discord).HandleAsync(payload, default);

        discord.Verify(d => d.EditDirectMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<string>()), Times.Never);
    }
}
