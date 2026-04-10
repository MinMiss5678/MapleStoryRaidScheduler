using Domain.Entities;
using Infrastructure.Services;
using Xunit;

namespace Test;

public class TeamSlotMergeServiceStaticTests
{
    // FindCommonDateTime tests

    [Fact]
    public void FindCommonDateTime_ShouldReturnNull_WhenMemberHasNoAvailability()
    {
        // Arrange
        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { DiscordId = 1 },
            new TeamSlotCharacter { DiscordId = 2 }  // 沒有 availability
        };

        var availabilities = new Dictionary<ulong, IEnumerable<PlayerAvailability>>
        {
            { 1, new List<PlayerAvailability> { new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) } } }
            // discordId 2 沒有 availability
        };

        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        // Act
        var result = TeamSlotMergeService.FindCommonDateTime(members, availabilities, period);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindCommonDateTime_ShouldReturnNull_WhenNoCommonTime()
    {
        // Arrange - 兩人完全沒有重疊時段
        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { DiscordId = 1 },
            new TeamSlotCharacter { DiscordId = 2 }
        };

        var availabilities = new Dictionary<ulong, IEnumerable<PlayerAvailability>>
        {
            {
                1, new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(11, 0) }
                }
            },
            {
                2, new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) }
                }
            }
        };

        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        // Act
        var result = TeamSlotMergeService.FindCommonDateTime(members, availabilities, period);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindCommonDateTime_ShouldReturnCommonDateTime_WhenOverlapExists()
    {
        // Arrange - 兩人週四 19-22 有重疊
        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { DiscordId = 1 },
            new TeamSlotCharacter { DiscordId = 2 }
        };

        var availabilities = new Dictionary<ulong, IEnumerable<PlayerAvailability>>
        {
            {
                1, new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(22, 0) }
                }
            },
            {
                2, new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(23, 0) }
                }
            }
        };

        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 週四
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        // Act
        var result = TeamSlotMergeService.FindCommonDateTime(members, availabilities, period);

        // Assert
        Assert.NotNull(result);
        // 共同時段起始是 19:00，所以應回傳包含 19:00 的時間
        var resultTpe = result.Value.ToOffset(TimeSpan.FromHours(8));
        Assert.Equal(19, resultTpe.Hour);
    }

    // TryMatchTemplate tests

    [Fact]
    public void TryMatchTemplate_ShouldReturnNull_WhenTooManyMembers()
    {
        // Arrange
        var jobCategories = new Dictionary<string, HashSet<string>>
        {
            { "任意", new HashSet<string> { "Hero", "Bishop", "Thief" } }
        };

        var template = new BossTemplate
        {
            Requirements = new List<BossTemplateRequirement>
            {
                new BossTemplateRequirement { JobCategory = "任意", Count = 2, Priority = 1 }
            }
        };

        // 3 members but requireMembers=2 → null
        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { CharacterName = "P1", Job = "Hero", AttackPower = 1000 },
            new TeamSlotCharacter { CharacterName = "P2", Job = "Bishop", AttackPower = 900 },
            new TeamSlotCharacter { CharacterName = "P3", Job = "Thief", AttackPower = 800 }
        };

        // Act
        var result = TeamSlotMergeService.TryMatchTemplate(members, template, jobCategories, 2);

        // Assert - 因為 result.Count(2) + remainingMembers.Count(1) > requireMembers(2) → null
        Assert.Null(result);
    }

    [Fact]
    public void TryMatchTemplate_ShouldFillEmptySlot_WhenNoMatchFound()
    {
        // Arrange
        var jobCategories = new Dictionary<string, HashSet<string>>
        {
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

        // 只有 Warrior，沒有 Mage → 應填入空位
        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { CharacterName = "P1", Job = "Hero", AttackPower = 1000 }
        };

        // Act
        var result = TeamSlotMergeService.TryMatchTemplate(members, template, jobCategories, 6);

        // Assert - 結果包含 P1 + 一個空位 (Mage)
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Contains(result, c => c.CharacterName == "P1");
        Assert.Contains(result, c => c.Job == "Mage" && c.DiscordName == "-");
    }
}
