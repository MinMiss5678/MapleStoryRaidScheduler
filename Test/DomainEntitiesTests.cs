using Application.DTOs;
using Domain.Entities;
using Xunit;

namespace Test;

public class DomainEntitiesTests
{
    [Fact]
    public void Boss_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var boss = new Boss();

        // Assert
        Assert.Equal(1, boss.RoundConsumption);
    }

    [Fact]
    public void Register_ShouldInitializeLists()
    {
        // Arrange & Act
        var register = new Register();

        // Assert
        Assert.NotNull(register.Availabilities);
        Assert.Empty(register.Availabilities);
    }

    [Fact]
    public void RegisterUpdateCommand_ShouldInitializeLists()
    {
        // Arrange & Act
        var command = new RegisterUpdateCommand();

        // Assert
        Assert.NotNull(command.Availabilities);
        Assert.NotNull(command.DeleteCharacterRegisterIds);
        Assert.Empty(command.Availabilities);
        Assert.Empty(command.DeleteCharacterRegisterIds);
    }

    [Fact]
    public void PlayerAvailability_ShouldSetProperties()
    {
        // Arrange
        var startTime = new TimeOnly(10, 0);
        var endTime = new TimeOnly(12, 0);

        // Act
        var availability = new PlayerAvailability
        {
            Id = 1,
            PlayerRegisterId = 100,
            Weekday = 1,
            StartTime = startTime,
            EndTime = endTime
        };

        // Assert
        Assert.Equal(1, availability.Id);
        Assert.Equal(100, availability.PlayerRegisterId);
        Assert.Equal(1, availability.Weekday);
        Assert.Equal(startTime, availability.StartTime);
        Assert.Equal(endTime, availability.EndTime);
    }

    [Fact]
    public void Character_ShouldSetProperties()
    {
        // Arrange & Act
        var character = new Character
        {
            Id = "char123",
            DiscordId = 123456789,
            Name = "Hero",
            Job = "Warrior",
            AttackPower = 50000
        };

        // Assert
        Assert.Equal("char123", character.Id);
        Assert.Equal((ulong)123456789, character.DiscordId);
        Assert.Equal("Hero", character.Name);
        Assert.Equal("Warrior", character.Job);
        Assert.Equal(50000, character.AttackPower);
    }

    [Fact]
    public void Period_ShouldSetProperties()
    {
        // Arrange & Act
        var now = DateTimeOffset.UtcNow;
        var period = new Period
        {
            Id = 1,
            StartDate = now,
            EndDate = now.AddDays(7)
        };

        // Assert
        Assert.Equal(1, period.Id);
        Assert.Equal(now, period.StartDate);
        Assert.Equal(now.AddDays(7), period.EndDate);
    }

    [Fact]
    public void TeamSlot_ShouldSetProperties()
    {
        // Arrange & Act
        var now = DateTimeOffset.UtcNow;
        var teamSlot = new TeamSlot
        {
            Id = 1,
            BossId = 2,
            BossName = "Zakum",
            SlotDateTime = now,
            TemplateId = 5,
            IsTemporary = true,
            IsPublished = false
        };

        // Assert
        Assert.Equal(1, teamSlot.Id);
        Assert.Equal(2, teamSlot.BossId);
        Assert.Equal("Zakum", teamSlot.BossName);
        Assert.Equal(now, teamSlot.SlotDateTime);
        Assert.Equal(5, teamSlot.TemplateId);
        Assert.True(teamSlot.IsTemporary);
        Assert.False(teamSlot.IsPublished);
        Assert.NotNull(teamSlot.Characters);
    }

    [Fact]
    public void TeamSlotUpdateCommand_ShouldInitializeLists()
    {
        // Arrange & Act
        var command = new TeamSlotUpdateCommand();

        // Assert
        Assert.NotNull(command.Characters);
        Assert.NotNull(command.DeleteTeamSlotCharacterIds);
    }
}
