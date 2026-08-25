using Application.Exceptions;
using Application.Interface;
using Infrastructure.Discord;
using Moq;
using Presentation.Infrastructure.Discord.Handlers;
using Xunit;

namespace Test;

/// <summary>
/// 團隊動作互動 handler 的 Handle seam（不碰 DSharpPlus）：三族六動作分派 + 例外分流。
/// 薄殼（收 ComponentInteractionCreated → deferred ack → 編輯訊息）無法自動化 → 留本機真 bot 手動驗。
/// </summary>
public class TeamActionInteractionHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITeamLeaderService> _svc = new();
    private TeamActionInteractionHandler Handler => new(_uow.Object, _svc.Object);

    [Fact]
    public async Task 邀請_接受_走AcceptInvite_commit()
    {
        var r = await Handler.HandleAsync(TeamActionFamily.Invite, true, 5, 100UL);
        _svc.Verify(s => s.AcceptInviteAsync(5, 100UL), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Once);
        Assert.Contains("已接受邀請", r);
    }

    [Fact]
    public async Task 邀請_拒絕_走DeclineInvite()
    {
        var r = await Handler.HandleAsync(TeamActionFamily.Invite, false, 5, 100UL);
        _svc.Verify(s => s.DeclineInviteAsync(5, 100UL), Times.Once);
        Assert.Contains("已拒絕邀請", r);
    }

    [Fact]
    public async Task 申請_核准_走Approve()
    {
        var r = await Handler.HandleAsync(TeamActionFamily.Application, true, 8, 200UL);
        _svc.Verify(s => s.ApproveAsync(8, 200UL), Times.Once);
        Assert.Contains("已核准", r);
    }

    [Fact]
    public async Task 申請_拒絕_走Reject()
    {
        var r = await Handler.HandleAsync(TeamActionFamily.Application, false, 8, 200UL);
        _svc.Verify(s => s.RejectAsync(8, 200UL), Times.Once);
        Assert.Contains("已拒絕此申請", r);
    }

    [Fact]
    public async Task 轉讓_接受_走RespondTransfer_accept()
    {
        var r = await Handler.HandleAsync(TeamActionFamily.Transfer, true, 3, 300UL);
        _svc.Verify(s => s.RespondLeaderTransferAsync(3, 300UL, "accept"), Times.Once);
        Assert.Contains("已接受轉讓", r);
    }

    [Fact]
    public async Task 轉讓_拒絕_走RespondTransfer_decline()
    {
        var r = await Handler.HandleAsync(TeamActionFamily.Transfer, false, 3, 300UL);
        _svc.Verify(s => s.RespondLeaderTransferAsync(3, 300UL, "decline"), Times.Once);
        Assert.Contains("已拒絕轉讓", r);
    }

    [Fact]
    public async Task 非本人_Forbidden_rollback_友善提示()
    {
        _svc.Setup(s => s.ApproveAsync(It.IsAny<int>(), It.IsAny<ulong>()))
            .ThrowsAsync(new ForbiddenException("只有隊長能核准申請。"));
        var r = await Handler.HandleAsync(TeamActionFamily.Application, true, 8, 200UL);
        _uow.Verify(u => u.RollbackAsync(), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Never);
        Assert.Contains("不是給你的", r);
    }

    [Fact]
    public async Task 已失效_Business_rollback_轉達訊息()
    {
        _svc.Setup(s => s.AcceptInviteAsync(It.IsAny<int>(), It.IsAny<ulong>()))
            .ThrowsAsync(new BusinessException("隊伍已滿。"));
        var r = await Handler.HandleAsync(TeamActionFamily.Invite, true, 5, 100UL);
        _uow.Verify(u => u.RollbackAsync(), Times.Once);
        Assert.Contains("已滿", r);
    }

    [Fact]
    public async Task 非預期例外_rollback_後rethrow()
    {
        _svc.Setup(s => s.RespondLeaderTransferAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler.HandleAsync(TeamActionFamily.Transfer, true, 3, 300UL));
        _uow.Verify(u => u.RollbackAsync(), Times.Once);
    }
}
