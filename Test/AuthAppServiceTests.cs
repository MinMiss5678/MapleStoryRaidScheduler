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
            _roleMappingRepositoryMock.Object);
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
}
