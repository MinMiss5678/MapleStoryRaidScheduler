using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Application.Queries;
using Infrastructure.Services;
using Moq;
using Xunit;
using Application.DTOs;

using Infrastructure.Entities;
using Application.Interface;
using System.Linq.Expressions;
using Infrastructure.Dapper;

namespace Test;

public class TeamSlotServiceTests
{
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<ITeamSlotQuery> _teamSlotQueryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<IPlayerAvailabilityRepository> _playerAvailabilityRepositoryMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<ICharacterQuery> _characterQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<IJobCategoryRepository> _jobCategoryRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<DbContext> _dbContextMock;
    private readonly Mock<IRepository<PlayerDbModel>> _playerRepositoryMock;
    private readonly TeamSlotService _teamSlotService;

    public TeamSlotServiceTests()
    {
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _teamSlotQueryMock = new Mock<ITeamSlotQuery>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();
        _characterQueryMock = new Mock<ICharacterQuery>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _jobCategoryRepositoryMock = new Mock<IJobCategoryRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _dbContextMock = new Mock<DbContext>();
        _playerRepositoryMock = new Mock<IRepository<PlayerDbModel>>();

        _dbContextMock.Setup(u => u.Repository<PlayerDbModel>()).Returns(_playerRepositoryMock.Object);

        _teamSlotService = new TeamSlotService(
            _teamSlotRepositoryMock.Object,
            _teamSlotQueryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _playerAvailabilityRepositoryMock.Object,
            _periodQueryMock.Object,
            _characterQueryMock.Object,
            _bossRepositoryMock.Object,
            _jobCategoryRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _dbContextMock.Object
        );
    }

    [Fact]
    public async Task AutoAssignAsync_ShouldAssignToExistingSlot_WhenMatchFound()
    {
        // Arrange
        var discordId = 12345UL;
        var periodId = 1;
        var bossId = 10;
        var characterId = "char1";

        var register = new Register
        {
            DiscordId = discordId,
            PeriodId = periodId,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = characterId, BossId = bossId, Rounds = 1 }
            },
            Availabilities = new List<PlayerAvailability>
            {
                // 週四 20:00 (楓之谷週期開始通常是週四)
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }
            }
        };

        // 模擬現有的 TeamSlot
        // 假設 Period StartDate 是 2024-05-23 (週四)
        // SlotDateTime 2024-05-23 12:00 UTC = 20:00 TPE (UTC+8)
        var slotDateTime = new DateTimeOffset(2024, 5, 23, 12, 0, 0, TimeSpan.Zero); 
        var existingTeamSlot = new TeamSlot
        {
            Id = 100,
            BossId = bossId,
            SlotDateTime = slotDateTime,
            Characters = new List<TeamSlotCharacter>() // 空隊伍
        };

        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(periodId))
            .ReturnsAsync(new List<TeamSlot> { existingTeamSlot });

        _periodQueryMock.Setup(p => p.GetByIdAsync(periodId))
            .ReturnsAsync(new Period
            {
                Id = periodId,
                StartDate = new DateTimeOffset(2024, 5, 23, 0, 0, 0, TimeSpan.Zero), // 2024-05-23 08:00 TPE
                EndDate = new DateTimeOffset(2024, 5, 30, 0, 0, 0, TimeSpan.Zero)
            });

        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(new List<Character> 
            { 
                new Character { Id = characterId, Name = "Hero", Job = "Warrior", AttackPower = 1000 } 
            });

        _playerRepositoryMock.Setup(r => r.GetByIdAsync((long)discordId, It.IsAny<Expression<Func<PlayerDbModel, object>>[]>()))
            .ReturnsAsync(new PlayerDbModel { DiscordId = (long)discordId, DiscordName = "Player1" });

        // Act
        await _teamSlotService.AutoAssignAsync(register);

        // Assert
        _teamSlotCharacterRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(c => 
            c.CharacterId == characterId && c.DiscordId == discordId)), Times.Once);
    }

    [Fact]
    public async Task AutoAssignAsync_ShouldCreateNewTeam_OnThursday20_WhenMonToFri_20To00()
    {
        // Arrange
        var discordId = 24680UL;
        var periodId = 2;
        var bossId = 20;
        var characterId = "charX";

        var register = new Register
        {
            DiscordId = discordId,
            PeriodId = periodId,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = characterId, BossId = bossId, Rounds = 1 }
            },
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }, // Mon
                new PlayerAvailability { Weekday = 2, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }, // Tue
                new PlayerAvailability { Weekday = 3, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }, // Wed
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }, // Thu
                new PlayerAvailability { Weekday = 5, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }  // Fri
            }
        };

        // 無現有隊伍
        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(periodId))
            .ReturnsAsync(new List<TeamSlot>());

        // 人物/玩家
        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(new List<Character>
            {
                new Character { Id = characterId, Name = "HeroX", Job = "Warrior", AttackPower = 2000 }
            });
        _playerRepositoryMock.Setup(r => r.GetByIdAsync((long)discordId, It.IsAny<Expression<Func<PlayerDbModel, object>>[]>()))
            .ReturnsAsync(new PlayerDbModel { DiscordId = (long)discordId, DiscordName = "PlayerX" });

        // 期間：假設週期開始於某個週四（日期無關緊要，計算以週四為 0）
        var period = new Period
        {
            Id = periodId,
            StartDate = new DateTimeOffset(2024, 5, 23, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2024, 5, 30, 0, 0, 0, TimeSpan.Zero)
        };
        _periodQueryMock.Setup(p => p.GetByIdAsync(periodId))
            .ReturnsAsync(period);

        // Boss 無模板 => fallback 建 slot
        _bossRepositoryMock.Setup(b => b.GetTemplatesByBossIdAsync(bossId))
            .ReturnsAsync(new List<BossTemplate>());

        TeamSlot? created = null;
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>()))
            .Callback<TeamSlot>(ts => created = ts)
            .ReturnsAsync(999);

        // Act
        await _teamSlotService.AutoAssignAsync(register);

        // Assert
        Assert.NotNull(created);
        // 轉為台灣時間 (+8) 應為週四 20:00
        var local = created!.SlotDateTime.ToOffset(TimeSpan.FromHours(8));
        Assert.Equal(DayOfWeek.Thursday, local.DayOfWeek);
        Assert.Equal(20, local.Hour);
        Assert.Equal(0, local.Minute);
    }

    [Fact]
    public async Task GetNextSlotDate_ShouldBeWithinPeriod_MonToFri_20To00()
    {
        // Arrange
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 2026-04-02 08:00 TPE
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        var availMon = new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(20, 0) };
        var availThuMidnight = new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(0, 0) };
        var availThu8PM = new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0) };

        // Act
        var resultMon = _teamSlotService.GetNextSlotDatePublic(availMon, period);
        var resultThuMid = _teamSlotService.GetNextSlotDatePublic(availThuMidnight, period);
        var resultThu8PM = _teamSlotService.GetNextSlotDatePublic(availThu8PM, period);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 4, 6, 20, 0, 0, TimeSpan.FromHours(8)), resultMon);
        Assert.Equal(new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.FromHours(8)), resultThuMid);
        Assert.Equal(new DateTimeOffset(2026, 4, 2, 20, 0, 0, TimeSpan.FromHours(8)), resultThu8PM);
    }

    [Fact]
    public async Task GetBestAvailability_ShouldPreferThu8PM_OverThuMidnight()
    {
        // Arrange
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 2026-04-02 08:00 TPE
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        var register = new Register
        {
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(0, 0) },
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0) }
            }
        };

        // Act
        // 使用反射呼叫私有方法 GetBestAvailability
        var method = typeof(TeamSlotService).GetMethod("GetBestAvailability", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (PlayerAvailability)method.Invoke(_teamSlotService, new object[] { register, period });

        // Assert
        Assert.Equal(20, result.StartTime.Hour);
    }

    [Fact]
    public void IsInJobCategory_ShouldReturnTrue_WhenJobMatchesCategory()
    {
        // Arrange
        var jobCategories = new Dictionary<string, HashSet<string>>
        {
            { "Warrior", new HashSet<string> { "Hero", "Paladin", "Dark Knight" } },
            { "Mage", new HashSet<string> { "Fire/Poison", "Ice/Lightning", "Bishop" } }
        };

        // Act & Assert
        Assert.True(_teamSlotService.IsInJobCategory("Hero", "Warrior", jobCategories));
        Assert.True(_teamSlotService.IsInJobCategory("Bishop", "Mage", jobCategories));
        Assert.False(_teamSlotService.IsInJobCategory("Hero", "Mage", jobCategories));
        Assert.False(_teamSlotService.IsInJobCategory("Thief", "Warrior", jobCategories));
    }

    [Fact]
    public void TryMatchTemplate_ShouldReturnMatchedCharacters_WhenRequirementsMet()
    {
        // Arrange
        var jobCategories = new Dictionary<string, HashSet<string>>
        {
            { "任意", new HashSet<string> { "Hero", "Bishop" } },
            { "Warrior", new HashSet<string> { "Hero" } },
            { "Mage", new HashSet<string> { "Bishop" } }
        };

        var template = new BossTemplate
        {
            Requirements = new List<BossTemplateRequirement>
            {
                new BossTemplateRequirement { JobCategory = "Warrior", Count = 1, Priority = 1 },
                new BossTemplateRequirement { JobCategory = "Mage", Count = 1, Priority = 2 }
            }
        };

        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { CharacterName = "P1", Job = "Hero" },
            new TeamSlotCharacter { CharacterName = "P2", Job = "Bishop" }
        };

        // Act
        var result = _teamSlotService.TryMatchTemplate(members, template, jobCategories, 6);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        // 驗證成員被正確放入 slot
        Assert.Contains(result, c => c.CharacterName == "P1");
        Assert.Contains(result, c => c.CharacterName == "P2");
    }
    [Fact]
    public async Task AutoAssignAsync_ShouldCreateNewSlot_WhenTeamNotFull()
    {
        // Arrange
        var discordId = 12345UL;
        var periodId = 1;
        var bossId = 10;
        var characterId = "char1";

        var register = new Register
        {
            DiscordId = discordId,
            PeriodId = periodId,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = characterId, BossId = bossId, Rounds = 1 }
            },
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }
            }
        };

        // 模擬現有的 TeamSlot，已有 1 人
        var slotDateTime = new DateTimeOffset(2024, 5, 23, 20, 0, 0, TimeSpan.FromHours(8)); 
        var existingTeamSlot = new TeamSlot
        {
            Id = 100,
            BossId = bossId,
            SlotDateTime = slotDateTime.ToOffset(TimeSpan.Zero),
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter 
                { 
                    Id = 1, 
                    CharacterId = "other_char", 
                    Job = "Support", 
                    TeamSlotId = 100 
                }
            }
        };

        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(periodId))
            .ReturnsAsync(new List<TeamSlot> { existingTeamSlot });

        _periodQueryMock.Setup(p => p.GetByIdAsync(periodId))
            .ReturnsAsync(new Period
            {
                Id = periodId,
                StartDate = new DateTimeOffset(2024, 5, 23, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2024, 5, 30, 0, 0, 0, TimeSpan.Zero)
            });

        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(new List<Character> 
            { 
                new Character { Id = characterId, Name = "Dealer", Job = "NightLord", AttackPower = 2000 } 
            });

        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<JobCategory> { new JobCategory { CategoryName = "Support", JobName = "Bishop" } });

        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(bossId))
            .ReturnsAsync(new List<BossTemplate>());

        // Act
        await _teamSlotService.AutoAssignAsync(register);

        // Assert
        // 因為隊伍尚未滿，應該直接加入現有隊伍，呼叫 CreateAsync 新增成員
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlot>()), Times.Never);
        _teamSlotCharacterRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(c => 
            c.CharacterId == characterId && c.TeamSlotId == 100)), Times.Once);
        
        // 驗證成員被加入到 list 中
        Assert.Contains(existingTeamSlot.Characters, c => c.CharacterId == characterId);
    }
}
