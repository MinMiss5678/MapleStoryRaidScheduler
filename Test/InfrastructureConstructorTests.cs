using System.Data;
using Application.Events;
using Application.Interface;
using Application.Options;
using Application.Queries;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Test;

/// <summary>
/// 測試 Repository、Query、BackgroundJob 構造函式 - 確保 DI 注入路徑有覆蓋率
/// </summary>
public class InfrastructureConstructorTests
{
    private static DbContext CreateDbContext()
    {
        var mockConn = new Mock<IDbConnection>();
        return new DbContext(mockConn.Object);
    }

    // ========== Repositories ==========

    [Fact]
    public void BossRepository_Constructor_InitializesCorrectly()
    {
        var repo = new BossRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void CharacterRegisterRepository_Constructor_InitializesCorrectly()
    {
        var repo = new CharacterRegisterRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void CharacterRepository_Constructor_InitializesCorrectly()
    {
        var repo = new CharacterRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void DiscordRoleMappingRepository_Constructor_InitializesCorrectly()
    {
        var repo = new DiscordRoleMappingRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void JobCategoryRepository_Constructor_InitializesCorrectly()
    {
        var repo = new JobCategoryRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void PeriodRepository_Constructor_InitializesCorrectly()
    {
        var repo = new PeriodRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void PlayerAvailabilityRepository_Constructor_InitializesCorrectly()
    {
        var repo = new PlayerAvailabilityRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void PlayerRegisterRepository_Constructor_InitializesCorrectly()
    {
        var repo = new PlayerRegisterRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void PlayerRepository_Constructor_InitializesCorrectly()
    {
        var repo = new PlayerRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void SessionRepository_Constructor_InitializesCorrectly()
    {
        var repo = new SessionRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void TeamSlotCharacterRepository_Constructor_InitializesCorrectly()
    {
        var repo = new TeamSlotCharacterRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    [Fact]
    public void TeamSlotRepository_Constructor_InitializesCorrectly()
    {
        var repo = new TeamSlotRepository(CreateDbContext());
        Assert.NotNull(repo);
    }

    // ========== Queries ==========

    [Fact]
    public void CharacterQuery_Constructor_InitializesCorrectly()
    {
        var query = new CharacterQuery(CreateDbContext());
        Assert.NotNull(query);
    }

    [Fact]
    public void SessionQuery_Constructor_InitializesCorrectly()
    {
        var query = new SessionQuery(CreateDbContext());
        Assert.NotNull(query);
    }

    [Fact]
    public void TeamSlotQuery_Constructor_InitializesCorrectly()
    {
        var query = new TeamSlotQuery(CreateDbContext());
        Assert.NotNull(query);
    }

    [Fact]
    public void PlayerRegisterQuery_Constructor_InitializesCorrectly()
    {
        var periodQueryMock = new Mock<IPeriodQuery>();
        var query = new PlayerRegisterQuery(periodQueryMock.Object, CreateDbContext());
        Assert.NotNull(query);
    }

    // ========== BackgroundJobs ==========

    [Fact]
    public void DailyNotificationService_Constructor_InitializesCorrectly()
    {
        var discordMock = new Mock<IDiscordService>();
        var teamSlotQueryMock = new Mock<ITeamSlotQuery>();
        var service = new DailyNotificationService(discordMock.Object, teamSlotQueryMock.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void WeeklyPeriodJob_Constructor_InitializesCorrectly()
    {
        var loggerMock = new Mock<ILogger<WeeklyPeriodJob>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var job = new WeeklyPeriodJob(loggerMock.Object, serviceProviderMock.Object);
        Assert.NotNull(job);
    }

    [Fact]
    public void RegistrationDeadlineJob_Constructor_InitializesCorrectly()
    {
        var loggerMock = new Mock<ILogger<RegistrationDeadlineJob>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var notifier = new ConfigChangeNotifier();
        var optionsMock = new Mock<IOptions<AppOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new AppOptions { AppUrl = "http://test" });

        var job = new RegistrationDeadlineJob(loggerMock.Object, serviceProviderMock.Object, notifier, optionsMock.Object);
        Assert.NotNull(job);
    }
}
