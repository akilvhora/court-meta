using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace CourtMetaAPI.Services;

/// <summary>
/// Lightweight per-endpoint telemetry: one NDJSON line per request, written
/// to <c>logs/court-meta-yyyy-MM-dd.log</c> next to the running exe.
///
/// Designed for the operator who wants to know "what's slow / what's
/// 401-retrying" without taking on a full logging-framework dependency. Lines
/// are easy to grep and easy to feed into <c>jq</c>.
///
/// A single writer task drains the bounded channel; producers never block on
/// I/O. If the background writer falls behind, oldest events are dropped
/// silently (BoundedChannelFullMode.DropOldest) — telemetry should never take
/// down the API.
/// </summary>
public class TelemetryService : IDisposable
{
    private readonly Channel<TelemetryEvent> _channel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<TelemetryService> _logger;
    private readonly string _logDir;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TelemetryService(ILogger<TelemetryService> logger, IHostEnvironment env)
    {
        _logger = logger;
        _logDir = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(_logDir);

        _channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(capacity: 1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _writerTask = Task.Run(WriteLoopAsync);
    }

    public void Track(TelemetryEvent evt)
    {
        // Producers never block — channel drops oldest when full.
        _channel.Writer.TryWrite(evt);
    }

    public void TrackTokenRefresh(string endpoint)
        => Track(new TelemetryEvent { Type = "token-refresh", Endpoint = endpoint });

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    var path = Path.Combine(_logDir, $"court-meta-{DateTime.UtcNow:yyyy-MM-dd}.log");
                    var line = JsonSerializer.Serialize(evt, JsonOptions) + Environment.NewLine;
                    await File.AppendAllTextAsync(path, line, _cts.Token);
                }
                catch (Exception ex)
                {
                    // Don't recurse into TelemetryService logging if file IO fails.
                    _logger.LogWarning(ex, "telemetry write failed");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { _writerTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        _cts.Dispose();
    }
}

public class TelemetryEvent
{
    public string Type { get; set; } = "request";          // request | token-refresh
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Endpoint { get; set; }
    public string? Method { get; set; }
    public int? StatusCode { get; set; }
    public double? LatencyMs { get; set; }
    public string? Error { get; set; }
}
