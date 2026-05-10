using System.Text.Json;
using System.Text.Json.Nodes;
using CourtMetaAPI.Middleware;
using CourtMetaAPI.Services.Endpoints;
using CourtMetaAPI.Services.Licensing;
using CourtMetaAPI.Services.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CourtMetaAPI.Filters;

/// <summary>
/// After the action returns, decides whether to reshape the body into the
/// paid envelope <c>{ schemaVersion, parsed[, raw] }</c>. The decision is:
/// — endpoint has <see cref="ParseableEndpointAttribute"/>; AND
/// — registry says the endpoint is parseable (mapping config is non-null); AND
/// — license claims include <c>parse:&lt;key&gt;</c> (or <c>parse:*</c>).
///
/// Failure modes degrade silently to raw passthrough. The
/// <c>X-Court-Meta-Parse</c> header tells the caller why — useful for
/// debugging without poking around in the body.
/// </summary>
public sealed class ParseableEndpointFilter : IAsyncResultFilter
{
    private readonly MappingEngine _engine;
    private readonly MappingConfigStore _configs;
    private readonly ILogger<ParseableEndpointFilter> _log;

    public ParseableEndpointFilter(MappingEngine engine, MappingConfigStore configs, ILogger<ParseableEndpointFilter> log)
    {
        _engine = engine;
        _configs = configs;
        _log = log;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var attr = context.ActionDescriptor.EndpointMetadata.OfType<ParseableEndpointAttribute>().FirstOrDefault();
        if (attr is null)
        {
            await next();
            return;
        }

        var def = EndpointRegistry.ByKey(attr.EndpointKey);
        if (def is null || !def.IsParseable)
        {
            // Attribute is on the action but the registry says raw-only — log and pass through.
            // This means an attribute typo or registry drift; the EndpointRegistryTests catch it
            // in CI but we still degrade gracefully at runtime.
            context.HttpContext.Response.Headers["X-Court-Meta-Parse"] = "registry-mismatch";
            await next();
            return;
        }

        var state = LicenseMiddleware.GetState(context.HttpContext);
        if (state.Status != LicenseStatus.Valid || state.Claims is null || !state.Claims.HasScope(def.ScopeName))
        {
            context.HttpContext.Response.Headers["X-Court-Meta-Parse"] = "scope-missing";
            await next();
            return;
        }

        // Only OkObjectResult shapes are eligible — error responses shouldn't be
        // mapped through the parsed envelope.
        if (context.Result is not ObjectResult ok || ok.Value is null || ok.StatusCode is < 200 or >= 300)
        {
            await next();
            return;
        }

        JsonNode? rawNode;
        try
        {
            rawNode = JsonSerializer.SerializeToNode(ok.Value);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "parsed-envelope: failed to serialise raw payload for {Endpoint}", def.Key);
            context.HttpContext.Response.Headers["X-Court-Meta-Parse"] = "serialise-failed";
            await next();
            return;
        }

        var config = _configs.TryGet(def.MappingConfig!);
        if (config is null)
        {
            _log.LogWarning("parsed-envelope: missing mapping config {Cfg}", def.MappingConfig);
            context.HttpContext.Response.Headers["X-Court-Meta-Parse"] = "config-missing";
            await next();
            return;
        }

        JsonObject envelope;
        try
        {
            var result = _engine.Apply(config, rawNode);
            envelope = new JsonObject
            {
                ["schemaVersion"] = def.SchemaVersion,
                ["parsed"]        = result.Result
            };
        }
        catch (Exception ex)
        {
            // Soft-fail policy from the design doc: never 500 on a parse error;
            // caller still gets the raw payload plus an error marker.
            _log.LogError(ex, "parsed-envelope: engine threw on {Endpoint}", def.Key);
            envelope = new JsonObject
            {
                ["schemaVersion"] = def.SchemaVersion,
                ["parsed"]        = null,
                ["parsedError"]   = ex.Message,
                ["raw"]           = rawNode
            };
            context.Result = new ObjectResult(envelope) { StatusCode = ok.StatusCode };
            context.HttpContext.Response.Headers["X-Court-Meta-Parse"] = "engine-error";
            await next();
            return;
        }

        if (RawIncludeRequested(context.HttpContext))
            envelope["raw"] = rawNode;

        context.Result = new ObjectResult(envelope) { StatusCode = ok.StatusCode };
        context.HttpContext.Response.Headers["X-Court-Meta-Parse"] = "applied";
        await next();
    }

    private static bool RawIncludeRequested(HttpContext ctx)
    {
        if (ctx.Request.Query.TryGetValue("include", out var inc))
        {
            foreach (var v in inc) if (string.Equals(v, "raw", StringComparison.OrdinalIgnoreCase)) return true;
        }
        // Accept: application/vnd.courtmeta+json; raw=1
        foreach (var accept in ctx.Request.Headers.Accept)
        {
            if (accept is null) continue;
            if (accept.Contains("raw=1", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
