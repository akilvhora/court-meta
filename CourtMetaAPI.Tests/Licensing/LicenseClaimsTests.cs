using CourtMetaAPI.Services.Licensing;
using Xunit;

namespace CourtMetaAPI.Tests.Licensing;

public class LicenseClaimsTests
{
    [Fact]
    public void ExactScopeMatch()
    {
        var c = MakeClaims("parse:cnr");
        Assert.True(c.HasScope("parse:cnr"));
        Assert.False(c.HasScope("parse:orders"));
    }

    [Fact]
    public void VerbWildcardMatchesAllSubScopes()
    {
        var c = MakeClaims("parse:*");
        Assert.True(c.HasScope("parse:cnr"));
        Assert.True(c.HasScope("parse:case-history"));
        Assert.True(c.HasScope("parse:anything-future"));
        // Different verb is not covered.
        Assert.False(c.HasScope("bulk:cnr"));
    }

    [Fact]
    public void NoScopesMeansNoAccess()
    {
        var c = MakeClaims();
        Assert.False(c.HasScope("parse:cnr"));
    }

    private static LicenseClaims MakeClaims(params string[] scopes) => new(
        Subject:   "test",
        IssuedAt:  DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
        Scopes:    new HashSet<string>(scopes, StringComparer.Ordinal),
        KeyId:     "test");
}
