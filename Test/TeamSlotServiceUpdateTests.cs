using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class TeamSlotServiceUpdateTests
{
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<ITeamSlotQuery> _teamSlotQueryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<IRegistrationLock> _registrationLockMock;
    private readonly TeamSlotService _teamSlotService;

    public TeamSlotServiceUpdateTests()
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

    [Fact]
    public async Task UpdateAsync_Admin_ShouldDeleteTeamSlots_WhenDeleteIdsProvided()
    {
        // Arrange
        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int> { 10, 20 },
            TeamSlots = new List<TeamSlotUpdateCommand>()
        };

        // Act
        await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        // Assert
        _teamSlotCharacterRepositoryMock.Verify(r => r.DeleteByTeamSlotIdAsync(10), Times.Once);
        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(10), Times.Once);
        _teamSlotCharacterRepositoryMock.Verify(r => r.DeleteByTeamSlotIdAsync(20), Times.Once);
        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(20), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NonAdmin_ShouldThrow_WhenDeletingTeamSlot()
    {
        // Arrange
        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int> { 10 },
            TeamSlots = new List<TeamSlotUpdateCommand>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: false, currentDiscordId: 12345));
    }

    [Fact]
    public async Task UpdateAsync_Admin_ShouldCreateNewTemporaryTeamSlot()
    {
        // Arrange
        int newTeamSlotId = 55;
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>())).ReturnsAsync(newTeamSlotId);

        var character = new TeamSlotMemberDto { CharacterId = "c1", DiscordId = 12345 };
        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    BossId = 1,
                    PeriodId = 1,
                    SlotDateTime = DateTimeOffset.UtcNow,
                    Source = TeamSlotSource.Admin,
                    Characters = new List<TeamSlotMemberDto> { character },
                    DeleteTeamSlotCharacterIds = new List<int>()
                }
            }
        };

        // Act
        await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        // Assert
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlot>()), Times.Once);
        _teamSlotCharacterRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(c =>
            c.TeamSlotId == newTeamSlotId)), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NonAdmin_ShouldThrow_WhenCreatingTemporaryTeamSlot()
    {
        // Arrange
        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Source = TeamSlotSource.Admin,
                    Characters = new List<TeamSlotMemberDto>(),
                    DeleteTeamSlotCharacterIds = new List<int>()
                }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: false, currentDiscordId: 12345));
    }

    [Fact]
    public async Task UpdateAsync_ShouldSkip_WhenTeamSlotNotFound()
    {
        // Arrange
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((TeamSlot?)null);

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = 99,
                    Source = TeamSlotSource.Auto,
                    Characters = new List<TeamSlotMemberDto>(),
                    DeleteTeamSlotCharacterIds = new List<int>()
                }
            }
        };

        // Act：不中斷、不丟例外，改標記進衝突清單
        var result = await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        Assert.Contains(99, result.ConflictedTeamSlotIds);
    }

    [Fact]
    public async Task UpdateAsync_NonAdmin_ShouldThrow_WhenDeletingOtherPersonsCharacter()
    {
        // Arrange
        ulong currentDiscordId = 12345;
        ulong otherDiscordId = 99999;
        int teamSlotId = 1;
        int charSlotId = 5;

        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = charSlotId, DiscordId = otherDiscordId, DiscordName = "", Job = "", CharacterId = "other" }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    Source = TeamSlotSource.Auto,
                    DeleteTeamSlotCharacterIds = new List<int> { charSlotId },
                    Characters = new List<TeamSlotMemberDto>()
                }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: false, currentDiscordId: currentDiscordId));
    }

    [Fact]
    public async Task UpdateAsync_NonAdmin_ShouldThrow_WhenAddingCharacterForOthers()
    {
        // Arrange
        ulong currentDiscordId = 12345;
        ulong otherDiscordId = 99999;
        int teamSlotId = 1;

        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter>()
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    Source = TeamSlotSource.Auto,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    // Id == null → new character, but DiscordId != currentDiscordId
                    Characters = new List<TeamSlotMemberDto>
                    {
                        new TeamSlotMemberDto { Id = null, DiscordId = otherDiscordId, CharacterId = "other" }
                    }
                }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: false, currentDiscordId: currentDiscordId));
    }

    [Fact]
    public async Task UpdateAsync_ShouldAcquireTeamSlotEditLock_ForExistingTeamSlot()
    {
        // 併發控制：編輯既有隊伍前必須序列化取鎖，擋同瞬間兩請求的 TOCTOU race
        int teamSlotId = 7;
        var existingTeamSlot = new TeamSlot { Id = teamSlotId, Characters = new List<TeamSlotCharacter>() };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto>()
                }
            }
        };

        await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        _registrationLockMock.Verify(l => l.AcquireTeamSlotEditLockAsync(teamSlotId), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Admin_ShouldUpdateExistingCharacter()
    {
        // Arrange
        int teamSlotId = 1;
        int? charSlotId = 5;
        ulong discordId = 12345;

        var existingChar = new TeamSlotCharacter { Id = charSlotId, DiscordId = discordId, DiscordName = "", Job = "", CharacterId = "c1" };
        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter> { existingChar }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);
        _teamSlotCharacterRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TeamSlotCharacter>())).ReturnsAsync(true);

        var updatedChar = new TeamSlotMemberDto { Id = charSlotId, DiscordId = discordId, CharacterId = "c1-updated", Version = "v1" };
        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    Source = TeamSlotSource.Auto,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto> { updatedChar }
                }
            }
        };

        // Act
        var result = await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        // Assert
        _teamSlotCharacterRepositoryMock.Verify(r => r.UpdateAsync(It.Is<TeamSlotCharacter>(c => c.Version == "v1")), Times.Once);
        Assert.Empty(result.ConflictedTeamSlotIds);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReportConflict_WhenOptimisticLockVersionMismatch()
    {
        // 樂觀鎖：其他流程（如 merge 填空位）在期間動過這列，xmin 對不上 → repository 回 false
        // → 標記進衝突清單，不丟例外中斷、不覆寫掉別人的變更
        int teamSlotId = 1;
        int? charSlotId = 5;
        ulong discordId = 12345;

        var existingChar = new TeamSlotCharacter { Id = charSlotId, DiscordId = discordId, DiscordName = "", Job = "", CharacterId = "c1" };
        var existingTeamSlot = new TeamSlot { Id = teamSlotId, Characters = new List<TeamSlotCharacter> { existingChar } };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);
        _teamSlotCharacterRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TeamSlotCharacter>())).ReturnsAsync(false);

        var staleChar = new TeamSlotMemberDto { Id = charSlotId, DiscordId = discordId, CharacterId = "c1", Version = "stale-version" };
        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto> { staleChar }
                }
            }
        };

        var result = await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        Assert.Contains(teamSlotId, result.ConflictedTeamSlotIds);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenAddingDuplicateCharacterToTeam()
    {
        // 隊伍已有 c1，再新增 c1 → 應擋下重複加入（admin 也擋）
        int teamSlotId = 1;
        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, CharacterId = "c1", DiscordId = 111, DiscordName = "", Job = "" }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto>
                    {
                        new TeamSlotMemberDto { Id = null, CharacterId = "c1", DiscordId = 111 } // Id==null → 新增，且 c1 重複
                    }
                }
            }
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 111));
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenAddingCharacterExceedsCapacity()
    {
        // 隊伍容量 1、已有 1 人，admin 再新增一人 → 應擋下超額（原本這條路徑完全沒做容量檢查）
        int teamSlotId = 1;
        int bossId = 9;
        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            BossId = bossId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, CharacterId = "c1", DiscordId = 111, DiscordName = "", Job = "" }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Boss> { new Boss { Id = bossId, Name = "", RequireMembers = 1 } });

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto>
                    {
                        new TeamSlotMemberDto { Id = null, CharacterId = "c2", DiscordId = 222 }
                    }
                }
            }
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 111));
    }

    [Fact]
    public async Task UpdateAsync_NonAdmin_ShouldThrow_WhenFillingEmptySlotWithOthersCharacter()
    {
        // 一般玩家填空位，卻填入「別人的」角色（DiscordId 非本人、非 0）→ 應擋下
        ulong currentDiscordId = 111;
        int teamSlotId = 1;
        int emptySlotId = 5;
        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = emptySlotId, CharacterId = null, DiscordId = 0, DiscordName = "", Job = "" } // 空位
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);

        var request = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto>
                    {
                        new TeamSlotMemberDto { Id = emptySlotId, DiscordId = 99999, CharacterId = "cX" }
                    }
                }
            }
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _teamSlotService.UpdateAsync(request, isAdmin: false, currentDiscordId: currentDiscordId));
    }
}
