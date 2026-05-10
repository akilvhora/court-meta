using System.Security.Cryptography;

namespace CourtMetaAPI.Services.Licensing;

/// <summary>
/// Loads license-signing RSA public keys embedded as <c>*.pub.pem</c> resources.
/// The file stem (e.g. <c>cm-dev-2026.pub.pem</c> → <c>cm-dev-2026</c>) is the
/// <c>kid</c> a JWT must declare. Rotation = ship a new key, optionally drop
/// the old one a release later.
/// </summary>
public sealed class LicensePublicKeys
{
    private readonly Dictionary<string, RSA> _byKid;

    public LicensePublicKeys()
    {
        _byKid = new(StringComparer.Ordinal);
        var asm = typeof(LicensePublicKeys).Assembly;
        const string prefix = "CourtMetaAPI.Licensing.Keys.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var fileName = name[prefix.Length..];
            // strip ".pub.pem" — `kid` is the bare stem.
            const string suffix = ".pub.pem";
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var kid = fileName[..^suffix.Length];

            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Cannot load {name}");
            using var reader = new StreamReader(stream);
            var pem = reader.ReadToEnd();
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            _byKid[kid] = rsa;
        }
    }

    public RSA? TryGet(string kid)
        => _byKid.TryGetValue(kid, out var rsa) ? rsa : null;

    public IReadOnlyCollection<string> KnownKids => _byKid.Keys;
}
