using CourtMetaAPI.Services.Endpoints;
using CourtMetaAPI.Services.Licensing;

namespace CourtMeta.Tools.IssueLicense;

/// <summary>
/// cm-issue — internal license issuer. NOT shipped to customers. Run by
/// support / sales (in production, behind a Jenkins job that holds the
/// private key in a credential).
///
/// Validates scopes against EndpointRegistry so a typo (parse:cnar) is a hard
/// error instead of a runtime surprise on the customer's box.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);
            if (opts is null) { PrintUsage(); return 1; }

            var unknown = opts.Scopes.Where(s => !ScopeIsKnown(s)).ToArray();
            if (unknown.Length > 0)
            {
                Console.Error.WriteLine($"Unknown scopes: {string.Join(", ", unknown)}");
                Console.Error.WriteLine($"Allowed:        {string.Join(", ", AllScopes())}");
                return 2;
            }

            var keyPem = File.ReadAllText(opts.PrivateKeyPath);
            var now    = DateTimeOffset.UtcNow;
            var jwt = LicenseSigner.Sign(new LicenseRequest(
                Customer:  opts.Customer,
                Scopes:    opts.Scopes,
                KeyId:     opts.KeyId,
                IssuedAt:  now,
                ExpiresAt: now.AddDays(opts.Days)), keyPem);

            Console.WriteLine(jwt);
            Console.Error.WriteLine($"# customer={opts.Customer} scopes=[{string.Join(",", opts.Scopes)}] kid={opts.KeyId} days={opts.Days}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cm-issue: {ex.Message}");
            return 3;
        }
    }

    private static bool ScopeIsKnown(string scope)
    {
        // parse:* is always allowed (verb wildcard); concrete scopes must match the registry.
        if (scope == "parse:*") return true;
        return EndpointRegistry.ParseableScopes().Contains(scope, StringComparer.Ordinal);
    }

    private static IEnumerable<string> AllScopes()
        => new[] { "parse:*" }.Concat(EndpointRegistry.ParseableScopes());

    private sealed record Options(
        string         Customer,
        IReadOnlyList<string> Scopes,
        string         KeyId,
        int            Days,
        string         PrivateKeyPath);

    private static Options? ParseArgs(string[] args)
    {
        string? customer = null, kid = null, keyPath = null;
        var scopes = new List<string>();
        int days = 60;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--customer": customer = NextArg(args, ref i); break;
                case "--scopes":   scopes.AddRange(NextArg(args, ref i).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--kid":      kid = NextArg(args, ref i); break;
                case "--days":     days = int.Parse(NextArg(args, ref i)); break;
                case "--key":      keyPath = NextArg(args, ref i); break;
                case "--help":
                case "-h":         return null;
                default:           throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (string.IsNullOrEmpty(customer))      throw new ArgumentException("--customer is required");
        if (scopes.Count == 0)                   throw new ArgumentException("--scopes is required");
        if (string.IsNullOrEmpty(kid))           throw new ArgumentException("--kid is required");
        if (string.IsNullOrEmpty(keyPath))       throw new ArgumentException("--key is required");
        if (days <= 0 || days > 90)              throw new ArgumentException("--days must be in 1..90");
        return new Options(customer, scopes, kid, days, keyPath);
    }

    private static string NextArg(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing value after {args[i]}");
        return args[++i];
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            cm-issue — Court Meta license issuer (internal).

              cm-issue --customer <id> --scopes parse:cnr[,parse:orders] \
                       --kid <key-id> --days <1..90> --key <path-to-priv.pem>

            Outputs the JWT to stdout; an audit summary to stderr.
            """);
    }
}
