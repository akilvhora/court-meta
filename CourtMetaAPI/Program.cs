var builder = WebApplication.CreateBuilder(args);

// Allows the app to run as a Windows Service (auto-sets content root to exe directory)
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "Court Meta API";
});

// CORS: allow any origin but only the custom extension header triggers access.
// The actual gate is the middleware below — CORS just needs to be permissive
// enough not to block the extension's preflight.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();

// In-memory cache for quasi-static lookups (states, districts, complexes, case-types).
// Cuts repeat upstream calls when the extension is opened multiple times.
builder.Services.AddMemoryCache();

// Singleton so the JWT token is shared across all requests and refreshed in one place
builder.Services.AddSingleton<CourtMetaAPI.Services.TokenService>();

// Single transport over the eCourts mobile API. All controllers funnel through this.
builder.Services.AddScoped<CourtMetaAPI.Services.EcourtsClient>();
builder.Services.AddSingleton<CourtMetaAPI.Services.HistoryParser>();
builder.Services.AddSingleton<CourtMetaAPI.Services.CauseListParser>();
builder.Services.AddScoped<CourtMetaAPI.Services.AdvocateSearchService>();
builder.Services.AddSingleton<CourtMetaAPI.Services.TelemetryService>();

// Register a named HttpClient for eCourts API
builder.Services.AddHttpClient("eCourts", client =>
{
    client.BaseAddress = new Uri("https://app.ecourts.gov.in/ecourt_mobile_DC/");
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Linux; Android 10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.120 Mobile Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
    client.DefaultRequestHeaders.Add("Origin", "https://app.ecourts.gov.in");
    client.DefaultRequestHeaders.Add("Referer", "https://app.ecourts.gov.in/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// ── Static files (sample website) ─────────────────────────────────────────────
// Served from wwwroot/ at http://localhost:5000/
// No origin restriction — the browser loads these normally.
app.UseDefaultFiles();
app.UseStaticFiles();

// ── Per-request telemetry ──────────────────────────────────────────────────
// Times every /api/* request and pushes a TelemetryEvent into the writer
// channel. Exceptions and non-2xx responses still get logged with the status
// code so operators can spot upstream flapping.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    string? error = null;
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        error = ex.Message;
        throw;
    }
    finally
    {
        sw.Stop();
        var telemetry = context.RequestServices
            .GetService<CourtMetaAPI.Services.TelemetryService>();
        telemetry?.Track(new CourtMetaAPI.Services.TelemetryEvent
        {
            Type = "request",
            Endpoint = context.Request.Path.Value,
            Method = context.Request.Method,
            StatusCode = context.Response.StatusCode,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            Error = error
        });
    }
});

// ── Extension client guard ─────────────────────────────────────────────────────
// Applied only to /api/* routes.
// The Chrome extension sends  X-Court-Meta-Client: extension  on every request.
// Manifest V3 service workers do not reliably include an Origin header, so we
// use this custom header as the gate instead.
// OPTIONS (CORS preflight) is passed through so the browser can negotiate first.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    if (context.Request.Method == "OPTIONS")
    {
        await next();
        return;
    }

    var clientHeader = context.Request.Headers["X-Court-Meta-Client"].ToString();
    if (!string.Equals(clientHeader, "extension", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            "{\"success\":false,\"error\":\"Access denied. This API only accepts requests from the Court Meta Chrome extension.\"}");
        return;
    }

    await next();
});

app.UseCors();
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/.well-known/{**path}", () => Results.NoContent());
app.MapControllers();

// Bind to localhost only
app.Urls.Add("http://localhost:5000");

app.Run();
