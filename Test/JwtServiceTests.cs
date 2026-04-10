using Application.Options;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Test;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;
    private const string SecretKey = "this-is-a-test-secret-key-that-is-long-enough-for-hs256";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    public JwtServiceTests()
    {
        var options = new JwtOptions
        {
            SecretKey = SecretKey,
            SecretKeyFile = "",
            Issuer = Issuer,
            Audience = Audience
        };
        var optionsMock = new Mock<IOptions<JwtOptions>>();
        optionsMock.Setup(x => x.Value).Returns(options);
        _jwtService = new JwtService(optionsMock.Object);
    }

    [Fact]
    public void CreateToken_ShouldReturnNonEmptyToken()
    {
        // Arrange
        var user = new DiscordUser { Id = 12345, Name = "TestUser" };

        // Act
        var token = _jwtService.CreateToken(user);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
    }

    [Fact]
    public void CreateToken_ShouldContainDiscordIdClaim()
    {
        // Arrange
        var user = new DiscordUser { Id = 99999, Name = "TestUser" };

        // Act
        var token = _jwtService.CreateToken(user);
        var claims = _jwtService.ReadJsonWebToken(token);

        // Assert
        Assert.Equal(user.Id, claims.DiscordId);
    }

    [Fact]
    public void ValidateToken_ShouldReturnValid_ForFreshlyCreatedToken()
    {
        // Arrange
        var user = new DiscordUser { Id = 12345, Name = "TestUser" };
        var token = _jwtService.CreateToken(user, expireMinutes: 15);

        // Act
        var result = _jwtService.ValidateToken(token);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(user.Id, result.DiscordId);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void ValidateToken_ShouldReturnInvalid_ForExpiredToken()
    {
        // Arrange - create token that expires immediately (negative minutes = already expired)
        var user = new DiscordUser { Id = 12345, Name = "TestUser" };
        var token = _jwtService.CreateToken(user, expireMinutes: -1);

        // Act
        var result = _jwtService.ValidateToken(token);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public void ValidateToken_ShouldReturnInvalid_ForMalformedToken()
    {
        // Act
        var result = _jwtService.ValidateToken("not-a-valid-token");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public void ReadJsonWebToken_ShouldReturnCorrectDiscordId()
    {
        // Arrange
        var user = new DiscordUser { Id = 777777, Name = "TestUser" };
        var token = _jwtService.CreateToken(user);

        // Act
        var claims = _jwtService.ReadJsonWebToken(token);

        // Assert
        Assert.Equal(user.Id, claims.DiscordId);
    }
}
