using Application.DTOs;
using Application.Exceptions;
using Xunit;

namespace Test;

public class AppDtoExceptionTests
{
    // ===== Exceptions =====

    [Fact]
    public void BusinessException_HasCorrectMessage_AndInheritsFromAppException()
    {
        var ex = new BusinessException("業務規則錯誤");
        Assert.Equal("業務規則錯誤", ex.Message);
        Assert.IsAssignableFrom<AppException>(ex);
    }

    [Fact]
    public void ForbiddenException_HasCorrectMessage_AndInheritsFromAppException()
    {
        var ex = new ForbiddenException("權限不足");
        Assert.Equal("權限不足", ex.Message);
        Assert.IsAssignableFrom<AppException>(ex);
    }

    // ===== LoginResult =====

    [Fact]
    public void LoginResult_IsSession_TrueWhenSessionIdSet()
    {
        var result = new LoginResult { SessionId = "abc123" };
        Assert.True(result.IsSession);
        Assert.False(result.IsJwt);
    }

    [Fact]
    public void LoginResult_IsJwt_TrueWhenJwtTokenSet()
    {
        var result = new LoginResult { JwtToken = "eyJhbGci..." };
        Assert.True(result.IsJwt);
        Assert.False(result.IsSession);
    }

    [Fact]
    public void LoginResult_BothEmpty_BothFalse()
    {
        var result = new LoginResult();
        Assert.False(result.IsSession);
        Assert.False(result.IsJwt);
    }
}
