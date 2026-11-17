using Application.DTOs;
using Application.Interface;
using Application.Options;
using Application.Services;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Test;

public class AuthAppServiceTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IDiscordOAuthClient> _discordOAuthClientMock;
    private readonly Mock<IPlayerService> _playerServiceMock;
    private readonly Mock<IOptions<DiscordOptions>> _discordOptionsMock;
    private readonly Mock<IDiscordRoleMappingRepository> _roleMappingRepositoryMock;
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly AuthAppService _authAppService;
    private readonly DiscordOptions _discordOptions = new();

    public AuthAppServiceTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _discordOAuthClientMock = new Mock<IDiscordOAuthClient>();
        _playerServiceMock = new Mock<IPlayerService>();
        _roleMappingRepositoryMock = new Mock<IDiscordRoleMappingRepository>();
        _discordOptionsMock = new Mock<IOptions<DiscordOptions>>();
        _discordOptionsMock.Setup(x => x.Value).Returns(_discordOptions);
        // 預設：公會成員查詢回空（無身分組、無暱稱）→ 各測試需要身分組再覆寫
        _discordOAuthClientMock.Setup(x => x.GetGuildMemberAsync(It.IsAny<ulong>()))
            .ReturnsAsync(new GuildMemberDto());

        _authAppService = new AuthAppService(
            _authServiceMock.Object,
            _discordOAuthClientMock.Object,
            _playerServiceMock.Object,
            _roleMappingRepositoryMock.Object,
            _uowMock.Object);
    }

    [Fact]
    public async Task LoginAsync_WhenUserHasAdminRole_ReturnsSessionId()
    {
        // Arrange
        var code = "test-code";
        var user = new DiscordUser { Id = 12345, Name = "user-name" };
        var roles = new List<string> { "1" };
        var sessionId = "session-id";

        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code))
            .ReturnsAsync(user);
        _discordOAuthClientMock.Setup(x => x.GetGuildMemberAsync(user.Id))
            .ReturnsAsync(new GuildMemberDto { Roles = roles });
        _roleMappingRepositoryMock
            .Setup(x => x.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync("admin");
        _authServiceMock.Setup(x => x.CreateSessionAsync(user.Id))
            .ReturnsAsync(sessionId);

        // Act
        var result = await _authAppService.LoginAsync(code);

        // Assert
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(user.Id, result.DiscordId);
    }

    [Fact]
    public async Task LoginAsync_WhenUserHasUserRole_ReturnsJwtToken()
    {
        // Arrange
        var code = "test-code";
        var user = new DiscordUser { Id = 12345, Name = "user-name" };
        var roles = new List<string> { "2" };
        var jwtToken = "jwt-token";

        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code))
            .ReturnsAsync(user);
        _discordOAuthClientMock.Setup(x => x.GetGuildMemberAsync(user.Id))
            .ReturnsAsync(new GuildMemberDto { Roles = roles });
        _roleMappingRepositoryMock
            .Setup(x => x.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync("User");
        _authServiceMock.Setup(x => x.CreateJwt(user, It.IsAny<string>()))
            .Returns(jwtToken);

        // Act
        var result = await _authAppService.LoginAsync(code);

        // Assert
        Assert.Equal(jwtToken, result.JwtToken);
        Assert.Equal(user.Id, result.DiscordId);
        _playerServiceMock.Verify(x => x.CreateAsync(It.Is<Player>(p => p.DiscordId == user.Id)), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenRoleCannotResolve_ReturnsFailure()
    {
        // Arrange：非既有玩家、Discord 身分組也映射不到系統角色 → 登入失敗、不建 session/jwt
        var code = "test-code";
        var user = new DiscordUser { Id = 12345, Name = "user-name" };
        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code)).ReturnsAsync(user);
        _discordOAuthClientMock.Setup(x => x.GetGuildMemberAsync(user.Id)).ReturnsAsync(new GuildMemberDto());
        _roleMappingRepositoryMock
            .Setup(x => x.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync((string?)null);
        // _playerServiceMock.GetAsync 預設回 null（非既有玩家）

        // Act
        var result = await _authAppService.LoginAsync(code);

        // Assert
        Assert.False(result.IsSuccess);
        _authServiceMock.Verify(x => x.CreateSessionAsync(It.IsAny<ulong>()), Times.Never);
        _authServiceMock.Verify(x => x.CreateJwt(It.IsAny<DiscordUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenDiscordNameTooLong_ReturnsFailureAndDoesNotWritePlayer()
    {
        // Arrange：bot/共用登入路徑不經 WebApi DTO 驗證；異常超長名稱應在 choke point 擋下、不落 DB
        var code = "test-code";
        var user = new DiscordUser { Id = 12345, Name = new string('a', 101) };
        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code)).ReturnsAsync(user);

        // Act
        var result = await _authAppService.LoginAsync(code);

        // Assert
        Assert.False(result.IsSuccess);
        _playerServiceMock.Verify(x => x.CreateAsync(It.IsAny<Player>()), Times.Never);
        _authServiceMock.Verify(x => x.CreateSessionAsync(It.IsAny<ulong>()), Times.Never);
        _authServiceMock.Verify(x => x.CreateJwt(It.IsAny<DiscordUser>(), It.IsAny<string>()), Times.Never);
    }

    // ===== E 組：登入的 Discord 呼叫必須在交易之外（plans/2026-11-16-auth-401-reason-and-commit-before-response.md §四）=====
    // 交易開著時等 Discord 回應，那條 DB 連線會一直被佔住；Discord 一慢，連線池就會被耗盡。

    private List<string> RecordCallOrder(string role = "user")
    {
        var calls = new List<string>();
        var user = new DiscordUser { Id = 555, Name = "player" };
        _authServiceMock.Setup(x => x.ExchangeCodeAsync("code"))
            .Callback(() => calls.Add("Discord:ExchangeCode")).ReturnsAsync(user);
        _discordOAuthClientMock.Setup(x => x.GetGuildMemberAsync(555))
            .Callback(() => calls.Add("Discord:GetGuildMember")).ReturnsAsync(new GuildMemberDto { Roles = ["1"] });
        _roleMappingRepositoryMock.Setup(x => x.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>())).ReturnsAsync(role);
        _playerServiceMock.Setup(x => x.CreateAsync(It.IsAny<Player>()))
            .Callback(() => calls.Add("DB:CreatePlayer")).Returns(Task.CompletedTask);
        _authServiceMock.Setup(x => x.CreateSessionAsync(555))
            .Callback(() => calls.Add("DB:CreateSession")).ReturnsAsync("sid");
        _authServiceMock.Setup(x => x.CreateJwt(It.IsAny<DiscordUser>(), It.IsAny<string>())).Returns("jwt");
        _uowMock.Setup(x => x.BeginAsync()).Callback(() => calls.Add("Begin")).Returns(Task.CompletedTask);
        _uowMock.Setup(x => x.CommitAsync()).Callback(() => calls.Add("Commit")).Returns(Task.CompletedTask);
        _uowMock.Setup(x => x.RollbackAsync()).Callback(() => calls.Add("Rollback")).Returns(Task.CompletedTask);
        return calls;
    }

    [Theory]
    [InlineData("user",
        new[] { "Discord:ExchangeCode", "Discord:GetGuildMember", "Begin", "DB:CreatePlayer", "Commit" })]
    [InlineData("admin",
        new[]
        {
            "Discord:ExchangeCode", "Discord:GetGuildMember", "Begin", "DB:CreatePlayer", "DB:CreateSession", "Commit"
        })]
    public async Task E1_登入成功_Discord呼叫都在開交易之前_寫DB在交易內_commit一次(string role, string[] expected)
    {
        var calls = RecordCallOrder(role);

        var result = await _authAppService.LoginAsync("code");

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, calls);
    }

    [Fact]
    public async Task E2_Discord呼叫失敗_不開交易()
    {
        RecordCallOrder();
        _discordOAuthClientMock.Setup(x => x.GetGuildMemberAsync(555))
            .ThrowsAsync(new HttpRequestException("discord down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => _authAppService.LoginAsync("code"));

        _uowMock.Verify(x => x.BeginAsync(), Times.Never);
    }

    [Fact]
    public async Task E2_無法解析角色而登入失敗_不開交易()
    {
        RecordCallOrder(role: "");

        var result = await _authAppService.LoginAsync("code");

        Assert.False(result.IsSuccess);
        _uowMock.Verify(x => x.BeginAsync(), Times.Never);
    }

    [Fact]
    public async Task E3_寫DB失敗_rollback_例外往外拋_不commit()
    {
        var calls = RecordCallOrder();
        _playerServiceMock.Setup(x => x.CreateAsync(It.IsAny<Player>()))
            .ThrowsAsync(new InvalidOperationException("db failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _authAppService.LoginAsync("code"));

        Assert.Contains("Rollback", calls);
        Assert.DoesNotContain("Commit", calls);
    }
}
