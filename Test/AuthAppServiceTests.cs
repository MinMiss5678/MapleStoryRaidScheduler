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
        var token = new DiscordToken { AccessToken = "access-token" };
        var roles = new List<string> { "1" };
        var sessionId = "session-id";

        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code))
            .ReturnsAsync((user, token));
        _discordOAuthClientMock.Setup(x => x.GetUserRolesAsync(user.Id))
            .ReturnsAsync(roles);
        _roleMappingRepositoryMock
            .Setup(x => x.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync("admin");
        _authServiceMock.Setup(x => x.CreateSessionAsync(user.Id, token))
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
        var token = new DiscordToken { AccessToken = "access-token" };
        var roles = new List<string> { "2" };
        var jwtToken = "jwt-token";

        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code))
            .ReturnsAsync((user, token));
        _discordOAuthClientMock.Setup(x => x.GetUserRolesAsync(user.Id))
            .ReturnsAsync(roles);
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
        var token = new DiscordToken { AccessToken = "access-token" };

        _authServiceMock.Setup(x => x.ExchangeCodeAsync(code)).ReturnsAsync((user, token));
        _discordOAuthClientMock.Setup(x => x.GetUserRolesAsync(user.Id)).ReturnsAsync(new List<string>());
        _roleMappingRepositoryMock
            .Setup(x => x.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync((string?)null);
        // _playerServiceMock.GetAsync 預設回 null（非既有玩家）

        // Act
        var result = await _authAppService.LoginAsync(code);

        // Assert
        Assert.False(result.IsSuccess);
        _authServiceMock.Verify(x => x.CreateSessionAsync(It.IsAny<ulong>(), It.IsAny<DiscordToken>()), Times.Never);
        _authServiceMock.Verify(x => x.CreateJwt(It.IsAny<DiscordUser>(), It.IsAny<string>()), Times.Never);
    }
}
