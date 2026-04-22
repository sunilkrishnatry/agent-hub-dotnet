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
    [InlineData("name+tag@domain.co.uk")]
    [InlineData("user123@test-domain.io")]
    [InlineData("a@b.co")]
    public void UserIdPattern_ValidEmail_Matches(string userId)
    {
        Assert.Matches(AgentRoutes.UserIdPattern(), userId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just-a-string")]
    [InlineData("user@")]
    [InlineData("@domain.com")]
    [InlineData("not-a-guid-at-all")]
    [InlineData("550e8400e29b41d4a716446655440000")] // GUID without hyphens
    [InlineData("550e8400-e29b-41d4-a716")] // incomplete GUID
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("user@domain")] // no TLD
    public void UserIdPattern_InvalidFormat_DoesNotMatch(string userId)
    {
        Assert.DoesNotMatch(AgentRoutes.UserIdPattern(), userId);
    }
}
