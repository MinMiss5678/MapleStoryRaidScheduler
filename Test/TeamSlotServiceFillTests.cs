using Application.DTOs;
using Application.Exceptions;
using Application.Queries;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class TeamSlotServiceFillTests
{
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<ITeamSlotQuery> _teamSlotQueryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<IRegistrationLock> _registrationLockMock;
    private readonly TeamSlotService _teamSlotService;

    public TeamSlotServiceFillTests()
    {
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _teamSlotQueryMock = new Mock<ITeamSlotQuery>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _registrationLockMock = new Mock<IRegistrationLock>();

        _teamSlotService = new TeamSlotService(
            _teamSlotRepositoryMock.Object,
            _teamSlotQueryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _periodQueryMock.Object,
            _bossRepositoryMock.Object,
            _registrationLockMock.Object);
    }

    // FillSlotAsync 寫入後會重新查詢最新狀態（跟 UpdateAsync controller 同一套慣例，見
    // plans/2026-07-31-teamslot-fill-endpoint-separation.md），成功案例都要把這段查詢路徑接好。
    private void SetupReQuery(int bossId, int teamSlotId)
    {
        var period = new Period { Id = 1, StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _teamSlotQueryMock.Setup(q => q.GetByPeriodAndBossIdAsync(period, bossId)).ReturnsAsync(new List<TeamSlotCharacterDto>
        {
            new TeamSlotCharacterDto
            {
                TeamSlotId = teamSlotId,
                TeamSlotCharacterId = 999,
                BossId = bossId,
                BossName = "",
                SlotDateTime = DateTimeOffset.UtcNow,
                DiscordId = 3001,
                DiscordName = "P-Fill",
                CharacterId = "c3001",
                CharacterName = "CFill",
                Job = "Hero",
                AttackPower = 940
            }
        });
    }

    [Fact]
    public async Task FillSlotAsync_ShouldCreateCharacter_WithCurrentDiscordId_NotPayloadDiscordId()
    {
        // 迴歸重點：payload 型別上根本沒有 DiscordId 欄位，一律用登入身分寫入，不可能填成別人。
        ulong currentDiscordId = 3001;
        int teamSlotId = 1;
        int bossId = 9;

        var existingTeam = new TeamSlot
        {
            Id = teamSlotId,
            BossId = bossId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 5, DiscordId = 4001, DiscordName = "P-Dummy", Job = "Hero", CharacterId = "c4001" }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeam);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { Id = bossId, Name = "", RequireMembers = 6 });
        SetupReQuery(bossId, teamSlotId);

        var request = new TeamSlotFillRequest
        {
            TeamSlotId = teamSlotId,
            DiscordName = "P-Fill",
            CharacterId = "c3001",
            CharacterName = "CFill",
            Job = "Hero",
            AttackPower = 940,
            Rounds = 0
        };

        var result = await _teamSlotService.FillSlotAsync(request, currentDiscordId);

        Assert.Equal(teamSlotId, result.Id);

        _teamSlotCharacterRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(c =>
            c.DiscordId == currentDiscordId &&
            c.CharacterId == "c3001" &&
            c.TeamSlotId == teamSlotId &&
            c.DiscordName == "P-Fill" &&
            c.IsManual)), Times.Once);
        // 既有的 P-Dummy 那筆完全沒被碰過——這正是修這個 bug 的重點。
        _teamSlotCharacterRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    [Fact]
    public async Task FillSlotAsync_ShouldAcquireTeamSlotEditLock()
    {
        int teamSlotId = 7;
        var existingTeam = new TeamSlot { Id = teamSlotId, Characters = new List<TeamSlotCharacter>() };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeam);
        SetupReQuery(bossId: 0, teamSlotId);

        var request = new TeamSlotFillRequest { TeamSlotId = teamSlotId, CharacterId = "c1", Job = "Hero" };

        await _teamSlotService.FillSlotAsync(request, currentDiscordId: 111);

        _registrationLockMock.Verify(l => l.AcquireTeamSlotEditLockAsync(teamSlotId), Times.Once);
    }

    [Fact]
    public async Task FillSlotAsync_ShouldThrow_WhenTeamSlotNotFound()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((TeamSlot?)null);

        var request = new TeamSlotFillRequest { TeamSlotId = 99, CharacterId = "c1", Job = "Hero" };

        await Assert.ThrowsAsync<BusinessException>(() =>
            _teamSlotService.FillSlotAsync(request, currentDiscordId: 111));
    }

    [Fact]
    public async Task FillSlotAsync_ShouldThrow_WhenLockTimesOut()
    {
        _registrationLockMock.Setup(l => l.AcquireTeamSlotEditLockAsync(1))
            .ThrowsAsync(new AdvisoryLockTimeoutException("teamslot_edit lock timeout"));

        var request = new TeamSlotFillRequest { TeamSlotId = 1, CharacterId = "c1", Job = "Hero" };

        await Assert.ThrowsAsync<BusinessException>(() =>
            _teamSlotService.FillSlotAsync(request, currentDiscordId: 111));
    }

    [Fact]
    public async Task FillSlotAsync_ShouldThrow_WhenCharacterAlreadyInTeam()
    {
        int teamSlotId = 1;
        var existingTeam = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, CharacterId = "c1", DiscordId = 111, DiscordName = "", Job = "" }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeam);

        var request = new TeamSlotFillRequest { TeamSlotId = teamSlotId, CharacterId = "c1", Job = "Hero" };

        await Assert.ThrowsAsync<DomainException>(() =>
            _teamSlotService.FillSlotAsync(request, currentDiscordId: 111));
    }

    [Fact]
    public async Task FillSlotAsync_ShouldThrow_WhenReQueryFindsNoMatchingTeamSlot()
    {
        // 邊界情況：寫入成功後重新查詢，卻找不到剛剛那個 teamSlotId（極端情況，例如同時被刪除）。
        // 防禦性判斷要能正確擋下，不能讓 FirstOrDefault 被改成 First 後直接丟未預期的 InvalidOperationException。
        int teamSlotId = 1;
        int bossId = 9;
        var existingTeam = new TeamSlot
        {
            Id = teamSlotId,
            BossId = bossId,
            Characters = new List<TeamSlotCharacter>()
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeam);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { Id = bossId, Name = "", RequireMembers = 6 });

        // 重查回傳的清單裡沒有這個 teamSlotId（模擬「重查時剛好被刪掉」的極端情況）
        var period = new Period { Id = 1, StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _teamSlotQueryMock.Setup(q => q.GetByPeriodAndBossIdAsync(period, bossId)).ReturnsAsync(new List<TeamSlotCharacterDto>());

        var request = new TeamSlotFillRequest { TeamSlotId = teamSlotId, CharacterId = "c1", Job = "Hero" };

        await Assert.ThrowsAsync<BusinessException>(() =>
            _teamSlotService.FillSlotAsync(request, currentDiscordId: 111));
    }

    [Fact]
    public async Task FillSlotAsync_ShouldThrow_WhenTeamIsFull()
    {
        int teamSlotId = 1;
        int bossId = 9;
        var existingTeam = new TeamSlot
        {
            Id = teamSlotId,
            BossId = bossId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, CharacterId = "c1", DiscordId = 111, DiscordName = "", Job = "" }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeam);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { Id = bossId, Name = "", RequireMembers = 1 });

        var request = new TeamSlotFillRequest { TeamSlotId = teamSlotId, CharacterId = "c2", Job = "Hero" };

        await Assert.ThrowsAsync<DomainException>(() =>
            _teamSlotService.FillSlotAsync(request, currentDiscordId: 222));
    }
}
