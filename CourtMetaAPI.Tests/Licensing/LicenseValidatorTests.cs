using CourtMetaAPI.Services.Licensing;
using Xunit;

namespace CourtMetaAPI.Tests.Licensing;

public class LicenseValidatorTests
{
    private static readonly string PrivateKeyPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "dev-keys", "cm-dev-2026.priv.pem"));

    private const string Kid = "cm-dev-2026";

    [Fact]
    public void RejectsMissingHeader()
    {
        var v = new LicenseValidator(new LicensePublicKeys());
        var s = v.Validate(null, DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Missing, s.Status);
    }

    [Fact]
    public void RejectsBadScheme()
    {
        var v = new LicenseValidator(new LicensePublicKeys());
        var s = v.Validate("Basic abc", DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Invalid, s.Status);
    }

    [Fact]
    public void AcceptsValidlySignedJwt()
    {
        var jwt = TestJwtSigner.Sign(
            subject: "acme-corp",
            scopes:  new[] { "parse:cnr" },
            kid:     Kid,
            issuedAt:  DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30),
            privateKeyPath: PrivateKeyPath);

        var v = new LicenseValidator(new LicensePublicKeys());
        var s = v.Validate("Bearer " + jwt, DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Valid, s.Status);
        Assert.NotNull(s.Claims);
        Assert.Equal("acme-corp", s.Claims!.Subject);
        Assert.Contains("parse:cnr", s.Claims.Scopes);
        Assert.True(s.Claims.HasScope("parse:cnr"));
        Assert.False(s.Claims.HasScope("parse:orders"));
    }

    [Fact]
    public void RejectsExpiredJwt()
    {
        var jwt = TestJwtSigner.Sign(
            subject: "acme-corp",
            scopes:  new[] { "parse:cnr" },
            kid:     Kid,
            issuedAt:  DateTimeOffset.UtcNow.AddDays(-30),
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
            privateKeyPath: PrivateKeyPath);

        var v = new LicenseValidator(new LicensePublicKeys());
        var s = v.Validate("Bearer " + jwt, DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Expired, s.Status);
    }

    [Fact]
    public void RejectsUnknownKid()
    {
        var jwt = TestJwtSigner.Sign(
            subject: "acme-corp",
            scopes:  new[] { "parse:cnr" },
            kid:     "no-such-key",
            issuedAt:  DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30),
            privateKeyPath: PrivateKeyPath);

        var v = new LicenseValidator(new LicensePublicKeys());
        var s = v.Validate("Bearer " + jwt, DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Invalid, s.Status);
        Assert.Contains("Unknown signing key", s.Reason);
    }

    [Fact]
    public void RejectsTamperedSignature()
    {
        var jwt = TestJwtSigner.Sign(
            subject: "acme-corp",
            scopes:  new[] { "parse:cnr" },
            kid:     Kid,
            issuedAt:  DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30),
            privateKeyPath: PrivateKeyPath);

        // Flip a byte in the middle of the signature segment. Tampering the
        // last char would sometimes leave the decoded bytes unchanged because
        // base64url's final 4-char group has padding bits that absorb edits.
        var lastDot = jwt.LastIndexOf('.');
        var midIdx  = lastDot + 1 + (jwt.Length - lastDot - 1) / 2;
        var midChar = jwt[midIdx];
        var swapped = midChar == 'A' ? 'B' : 'A';
        var tampered = jwt[..midIdx] + swapped + jwt[(midIdx + 1)..];

        var v = new LicenseValidator(new LicensePublicKeys());
        var s = v.Validate("Bearer " + tampered, DateTimeOffset.UtcNow);
        Assert.Equal(LicenseStatus.Invalid, s.Status);
    }
}
