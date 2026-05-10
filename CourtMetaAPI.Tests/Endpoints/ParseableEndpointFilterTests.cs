using System.Text.Json.Nodes;
using CourtMetaAPI.Filters;
using CourtMetaAPI.Middleware;
using CourtMetaAPI.Services.Licensing;
using CourtMetaAPI.Services.Mapping;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtMetaAPI.Tests.Endpoints;

public class ParseableEndpointFilterTests
{
    private static readonly string PrivateKeyPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "dev-keys", "cm-dev-2026.priv.pem"));

    [Fact]
    public async Task ScopeMissing_PassesThroughRaw()
    {
        var ctx = MakeContext("cnr", licenseScopes: Array.Empty<string>(), payload: SamplePayload());
        var filter = MakeFilter();

        await filter.OnResultExecutionAsync(ctx, () => Task.FromResult(MakeExecutedContext(ctx)));

        Assert.Equal("scope-missing", ctx.HttpContext.Response.Headers["X-Court-Meta-Parse"]);
        // Result is unchanged (still the original ObjectResult with anon payload).
        Assert.IsType<ObjectResult>(ctx.Result);
        var body = ((ObjectResult)ctx.Result).Value;
        Assert.NotNull(body);
        // The raw response shape from the controller has a top-level "success" anon field.
        var node = System.Text.Json.JsonSerializer.SerializeToNode(body)!.AsObject();
        Assert.True(node.ContainsKey("success"));
        Assert.False(node.ContainsKey("schemaVersion"));
    }

    [Fact]
    public async Task ScopeGranted_AppliesParsedEnvelope()
    {
        var ctx = MakeContext("cnr", licenseScopes: new[] { "parse:cnr" }, payload: SamplePayload());
        var filter = MakeFilter();

        await filter.OnResultExecutionAsync(ctx, () => Task.FromResult(MakeExecutedContext(ctx)));

        Assert.Equal("applied", ctx.HttpContext.Response.Headers["X-Court-Meta-Parse"]);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        var node = (JsonObject)result.Value!;
        Assert.Equal("cnr/1", node["schemaVersion"]?.GetValue<string>());
        Assert.NotNull(node["parsed"]);
        Assert.False(node.ContainsKey("raw"), "raw should be omitted by default");
        // The mapping config produces cnrNumber from data.cino.
        Assert.Equal("MHPU010001232022", node["parsed"]!["cnrNumber"]!.GetValue<string>());
    }

    [Fact]
    public async Task IncludeRawQuery_AttachesRawAlongsideParsed()
    {
        var ctx = MakeContext("cnr", licenseScopes: new[] { "parse:cnr" }, payload: SamplePayload(),
                              query: "include=raw");
        var filter = MakeFilter();

        await filter.OnResultExecutionAsync(ctx, () => Task.FromResult(MakeExecutedContext(ctx)));

        var node = (JsonObject)((ObjectResult)ctx.Result!).Value!;
        Assert.NotNull(node["parsed"]);
        Assert.NotNull(node["raw"]);
    }

    [Fact]
    public async Task WildcardScope_AppliesEnvelope()
    {
        var ctx = MakeContext("cnr", licenseScopes: new[] { "parse:*" }, payload: SamplePayload());
        var filter = MakeFilter();

        await filter.OnResultExecutionAsync(ctx, () => Task.FromResult(MakeExecutedContext(ctx)));

        Assert.Equal("applied", ctx.HttpContext.Response.Headers["X-Court-Meta-Parse"]);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ParseableEndpointFilter MakeFilter()
        => new(new MappingEngine(), new MappingConfigStore(), NullLogger<ParseableEndpointFilter>.Instance);

    private static object SamplePayload() => new
    {
        success = true,
        data = new
        {
            cino = "MHPU010001232022",
            type = "case",
            history = new
            {
                pet_name = "Rajesh Kumar",
                res_name = "State of Maharashtra"
            }
        }
    };

    private static ResultExecutingContext MakeContext(
        string endpointKey,
        string[] licenseScopes,
        object payload,
        string? query = null)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        if (query is not null)
            http.Request.QueryString = new QueryString("?" + query);

        // Stash a license state with the requested scopes (Valid/Missing decision is in the filter).
        if (licenseScopes.Length > 0)
        {
            var claims = new LicenseClaims(
                Subject: "test",
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
                Scopes: new HashSet<string>(licenseScopes, StringComparer.Ordinal),
                KeyId: "test");
            http.Items[LicenseMiddleware.LicenseStateKey] = LicenseState.Valid(claims);
        }
        else
        {
            http.Items[LicenseMiddleware.LicenseStateKey] = LicenseState.Missing();
        }

        var attr = new ParseableEndpointAttribute(endpointKey);
        var actionDescriptor = new ActionDescriptor
        {
            EndpointMetadata = new List<object> { attr }
        };

        var actionContext = new ActionContext(http, new RouteData(), actionDescriptor);
        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new ObjectResult(payload) { StatusCode = 200 },
            controller: null!);
    }

    private static ResultExecutedContext MakeExecutedContext(ResultExecutingContext ctx)
        => new(ctx, ctx.Filters, ctx.Result, controller: null!);
}
