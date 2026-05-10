using CourtMetaAPI.Services.Licensing;

namespace CourtMeta.Tools.VerifyLicense;

/// <summary>
/// cm-license-verify — minimal probe used by the Inno Setup wizard page.
/// Reads a JWT from argv[0] (or stdin if argv empty), verifies it against the
/// embedded public keys, and emits a single status line to stdout.
///
/// Output format (machine-parseable, one key=value per token, space-separated):
///
///   status=valid customer=acme-corp kid=cm-2026-05 expires=2026-08-07 scopes=parse:cnr,parse:orders
///   status=expired reason="License expired on 2025-12-01T00:00:00.0000000+00:00"
///   status=invalid reason="Signature failed verification."
///   status=missing
///
/// Exit codes:
///   0 = valid
///   2 = expired
///   3 = invalid / missing
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        var token = args.Length > 0 ? args[0] : Console.In.ReadToEnd().Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Out.WriteLine("status=missing");
            return 3;
        }

        var keys = new LicensePublicKeys();
        var validator = new LicenseValidator(keys);
        var state = validator.Validate("Bearer " + token, DateTimeOffset.UtcNow);

        switch (state.Status)
        {
            case LicenseStatus.Valid:
                var c = state.Claims!;
                var scopes = string.Join(',', c.Scopes);
                Console.Out.WriteLine(
                    $"status=valid customer={Escape(c.Subject)} kid={c.KeyId} " +
                    $"expires={c.ExpiresAt:yyyy-MM-dd} scopes={scopes}");
                return 0;
            case LicenseStatus.Expired:
                Console.Out.WriteLine($"status=expired reason={Escape(state.Reason ?? "")}");
                return 2;
            default:
                Console.Out.WriteLine($"status=invalid reason={Escape(state.Reason ?? "unknown")}");
                return 3;
        }
    }

    private static string Escape(string s)
    {
        // Quote if it contains spaces — Inno Setup parses by token.
        return s.Contains(' ') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;
    }
}
