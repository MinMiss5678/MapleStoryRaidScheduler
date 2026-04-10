using Application.DTOs;
using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class AuthServiceTests
{
    private readonly Mock<IDiscordOAuthClient> _discordClientMock = new();
    private readonly Mock<ISessionService> _sessionServiceMock = new();
    private readonly Mock<IDiscordRoleMappingRepository> _roleMappingRepoMock = new();
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly Mock<IPlayerRepository> _playerRepoMock = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _authService = new AuthService(
            _discordClientMock.Object,
            _sessionServiceMock.Object,
            _roleMappingRepoMock.Object,
            _jwtServiceMock.Object,
            _playerRepoMock.Object);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ReturnsUserAndToken()
    {
        _discordClientMock.Setup(c => c.ExchangeCodeAsync("code123"))
            .ReturnsAsync(new DiscordTokenResponse
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                ExpiresIn = 3600
            });
        _discordClientMock.Setup(c => c.GetUserAsync("access"))
            .ReturnsAsync(new DiscordUserDto { Id = 111, Username = "TestUser" });

        var (user, token) = await _authService.ExchangeCodeAsync("code123");

        Assert.Equal(111UL, user.Id);
        Assert.Equal("TestUser", user.Name);
        Assert.Equal("access", token.AccessToken);
    }

    [Fact]
    public async Task CreateSessionAsync_DelegatesToSessionService()
    {
        var discordToken = new DiscordToken { AccessToken = "token", RefreshToken = "refresh", ExpiresIn = 3600 };
        _sessionServiceMock.Setup(s => s.CreateAsync(123UL, discordToken)).ReturnsAsync("session-id-abc");

        var result = await _authService.CreateSessionAsync(123UL, discordToken);

        Assert.Equal("session-id-abc", result);
    }

    [Fact]
    public void CreateJwt_DelegatesToJwtService()
    {
        var user = new DiscordUser { Id = 999UL, Name = "admin" };
        _jwtServiceMock.Setup(j => j.CreateToken(user, It.IsAny<int>())).Returns("jwt-token");

        var result = _authService.CreateJwt(user);

        Assert.Equal("jwt-token", result);
    }

    [Fact]
    public async Task DeleteSessionAsync_DelegatesToSessionService()
    {
        _sessionServiceMock.Setup(s => s.DeleteAsync("sid", "123")).ReturnsAsync(true);

        var result = await _authService.DeleteSessionAsync("sid", "123");

        Assert.True(result);
    }

    [Fact]
    public async Task RefreshToken_WhenRoleExists_ReturnsJwt()
    {
        ulong discordId = 555UL;
        _discordClientMock.Setup(c => c.GetUserRolesAsync(discordId))
            .ReturnsAsync(new List<string> { "123456789" });
        _roleMappingRepoMock.Setup(r => r.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync("Admin");
        _playerRepoMock.Setup(p => p.GetAsync(discordId))
            .ReturnsAsync(new Player { DiscordId = discordId, DiscordName = "AdminUser" });
        _jwtServiceMock.Setup(j => j.CreateToken(It.IsAny<DiscordUser>(), It.IsAny<int>()))
            .Returns("new-jwt");

        var result = await _authService.RefreshToken(discordId);

        Assert.Equal("new-jwt", result);
    }

    [Fact]
    public async Task RefreshToken_WhenNoRole_ReturnsNull()
    {
        ulong discordId = 666UL;
        _discordClientMock.Setup(c => c.GetUserRolesAsync(discordId))
            .ReturnsAsync(new List<string> { "not-a-number" });
        _roleMappingRepoMock.Setup(r => r.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync((string?)null);

        var result = await _authService.RefreshToken(discordId);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshToken_WithNonNumericRole_FilteredOut()
    {
        ulong discordId = 777UL;
        _discordClientMock.Setup(c => c.GetUserRolesAsync(discordId))
            .ReturnsAsync(new List<string> { "abc", "def" }); // 全部非數字
        _roleMappingRepoMock.Setup(r => r.ResolveRoleAsync(It.IsAny<IEnumerable<ulong>>()))
            .ReturnsAsync((string?)null);

        var result = await _authService.RefreshToken(discordId);

        Assert.Null(result);
    }
}
