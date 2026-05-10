using CourtMetaAPI.Services.Licensing;

namespace CourtMetaAPI.Middleware;

/// <summary>
/// Per-request license resolution. Order of precedence for finding the JWT:
///   1. <c>Authorization: Bearer …</c> header (caller-supplied)
///   2. installed license file (<c>%ProgramData%\CourtMeta\license.jwt</c>)
///
/// The header path lets internal callers and tests pin a specific license; the
/// file path is what production paid customers use. Either way the result is
/// stamped onto <c>HttpContext.Items[LicenseStateKey]</c> for downstream filters
/// and the token-status endpoint.
/// </summary>
public sealed class LicenseMiddleware
{
    public const string LicenseStateKey = "court-meta.license-state";

    private readonly RequestDelegate _next;
    private readonly LicenseValidator _validator;
    private readonly LicenseLoader _loader;
    private readonly ILogger<LicenseMiddleware> _log;

    public LicenseMiddleware(RequestDelegate next, LicenseValidator validator, LicenseLoader loader, ILogger<LicenseMiddleware> log)
    {
        _next = next;
        _validator = validator;
        _loader = loader;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only /api/* requests carry a license; static files, OPTIONS preflights
        // and the front-end never need one.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var state = ResolveState(context);
        context.Items[LicenseStateKey] = state;

        if (state.Status == LicenseStatus.Invalid)
            _log.LogWarning("license-invalid: {Reason}", state.Reason);

        await _next(context);
    }

    private LicenseState ResolveState(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader))
            return _validator.Validate(authHeader, DateTimeOffset.UtcNow);

        var installed = _loader.TryReadInstalledLicense();
        if (string.IsNullOrEmpty(installed))
            return LicenseState.Missing();

        return _validator.Validate("Bearer " + installed, DateTimeOffset.UtcNow);
    }

    public static LicenseState GetState(HttpContext context)
        => context.Items[LicenseStateKey] as LicenseState ?? LicenseState.Missing();
}
