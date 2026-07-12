using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
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
    private readonly TeamSlotService _teamSlotService;

    public TeamSlotServiceUpdateTests()
    {
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _teamSlotQueryMock = new Mock<ITeamSlotQuery>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();

        _teamSlotService = new TeamSlotService(
            _teamSlotRepositoryMock.Object,
            _teamSlotQueryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _periodQueryMock.Object);
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

        // Act & Assert (no exception)
        await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);
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
                new TeamSlotCharacter { Id = charSlotId, DiscordId = otherDiscordId, CharacterId = "other" }
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
    public async Task UpdateAsync_Admin_ShouldUpdateExistingCharacter()
    {
        // Arrange
        int teamSlotId = 1;
        int? charSlotId = 5;
        ulong discordId = 12345;

        var existingChar = new TeamSlotCharacter { Id = charSlotId, DiscordId = discordId, CharacterId = "c1" };
        var existingTeamSlot = new TeamSlot
        {
            Id = teamSlotId,
            Characters = new List<TeamSlotCharacter> { existingChar }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId)).ReturnsAsync(existingTeamSlot);

        var updatedChar = new TeamSlotMemberDto { Id = charSlotId, DiscordId = discordId, CharacterId = "c1-updated" };
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
        await _teamSlotService.UpdateAsync(request, isAdmin: true, currentDiscordId: 0);

        // Assert
        _teamSlotCharacterRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TeamSlotCharacter>()), Times.Once);
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
                new TeamSlotCharacter { Id = 1, CharacterId = "c1", DiscordId = 111 }
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
                new TeamSlotCharacter { Id = emptySlotId, CharacterId = null, DiscordId = 0 } // 空位
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
