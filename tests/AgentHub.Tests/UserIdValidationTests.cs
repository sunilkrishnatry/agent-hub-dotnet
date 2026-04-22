using AgentHub.API.Routes;

namespace AgentHub.Tests;

public class UserIdValidationTests
{
    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ABCDEF01-2345-6789-ABCD-EF0123456789")]
    [InlineData("abcdef01-2345-6789-abcd-ef0123456789")]
    public void UserIdPattern_ValidGuid_Matches(string userId)
    {
        Assert.Matches(AgentRoutes.UserIdPattern(), userId);
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@company.org")]
    [InlineData("user123@test-domain.io")]
    [InlineData("a@b.co")]
    public void UserIdPattern_ValidEmail_Matches(string userId)
    {
        Assert.Matches(AgentRoutes.UserIdPattern(), userId);
    }

    [Theory]
    [InlineData("jdoe")]
    [InlineData("admin")]
    [InlineData("user-123")]
    [InlineData("user_123")]
    [InlineData("user.name")]
    [InlineData("A")]
    [InlineData("x1234567890")]
    public void UserIdPattern_ValidSimpleId_Matches(string userId)
    {
        Assert.Matches(AgentRoutes.UserIdPattern(), userId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".startswithdot")]
    [InlineData("-startswithyphen")]
    [InlineData("_startsWithUnderscore")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public void UserIdPattern_InvalidFormat_DoesNotMatch(string userId)
    {
        Assert.DoesNotMatch(AgentRoutes.UserIdPattern(), userId);
    }

    [Fact]
    public void UserIdPattern_MaxLength128_Matches()
    {
        var userId = "a" + new string('b', 127);
        Assert.Equal(128, userId.Length);
        Assert.Matches(AgentRoutes.UserIdPattern(), userId);
    }

    [Fact]
    public void UserIdPattern_Over128_DoesNotMatchViaRegex()
    {
        // The regex anchors at 128 chars (start char + up to 127 more)
        var userId = "a" + new string('b', 128);
        Assert.Equal(129, userId.Length);
        Assert.DoesNotMatch(AgentRoutes.UserIdPattern(), userId);
    }
}
