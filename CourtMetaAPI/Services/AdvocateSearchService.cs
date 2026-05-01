using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace CourtMetaAPI.Services;

/// <summary>
/// Workflow B — advocate-name / bar-code search.
///
/// Two scopes:
///   - <c>court</c>: a single upstream call against <c>court_code_arr</c> the
///     caller already chose. Returns a flat result list (same shape as the
///     case/filing-number search in <see cref="Controllers.SearchController"/>).
///   - <c>state</c>: enumerate every district in the state, fan out one
///     advocate-search call per district (with that district's complexes
///     joined into <c>court_code_arr</c>), and stream progress events as each
///     district completes. Concurrency is bounded by a SemaphoreSlim so we
///     don't trigger the eCourts 401-storm under heavy fan-out.
/// </summary>
public class AdvocateSearchService
{
    /// <summary>
    /// Cap on simultaneous in-flight upstream calls during a state-wide fan-out.
    /// 8 is small enough to stay polite while still amortising token-refresh
    /// cost across districts. Bumping this without verifying token rotation
    /// behaviour is risky.
    /// </summary>
    private const int MaxParallelDistricts = 8;

    private readonly EcourtsClient _ecourts;
    private readonly ILogger<AdvocateSearchService> _logger;

    public AdvocateSearchService(EcourtsClient ecourts, ILogger<AdvocateSearchService> logger)
    {
        _ecourts = ecourts;
        _logger = logger;
    }

    // ─── Single-court (or single-district) search ────────────────────────────
    public async Task<(List<JsonObject> rows, JsonNode? raw, string? error)> SearchSingleAsync(
        AdvocateSearchRequest req,
        string courtCodeArr,
        CancellationToken ct)
    {
        var fields = BuildSearchFields(req, req.StateCode, req.DistCode, courtCodeArr);
        var (node, error) = await _ecourts.CallAsync("searchByAdvocateName.php", fields, ct: ct);
        if (error != null) return (new(), null, error);
        return (Flatten(node), node, null);
    }

    // ─── State-wide streaming search ─────────────────────────────────────────
    /// <summary>
    /// Enumerates districts in the state, fans out one advocate-search per
    /// district, and yields events as each district completes. Bounded by
    /// <see cref="MaxParallelDistricts"/>.
    /// </summary>
    public async IAsyncEnumerable<AdvocateSearchEvent> SearchStateAsync(
        AdvocateSearchRequest req,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 1. Districts
        var (districtsNode, dErr) = await _ecourts.CallAsync("districtWebService.php", new()
        {
            ["state_code"] = req.StateCode,
            ["test_param"] = "pending"
        }, ct: ct);

        if (dErr != null)
        {
            yield return new AdvocateSearchEvent { Type = "error", Error = dErr };
            yield break;
        }

        var districts = districtsNode?["districts"]?.AsArray()
            ?.Select(d => new {
                Code = d?["dist_code"]?.ToString(),
                Name = d?["dist_name"]?.ToString()
            })
            .Where(d => !string.IsNullOrEmpty(d.Code))
            .ToList() ?? new();

        yield return new AdvocateSearchEvent
        {
            Type = "start",
            TotalDistricts = districts.Count,
            StateCode = req.StateCode
        };

        if (districts.Count == 0)
        {
            yield return new AdvocateSearchEvent { Type = "complete", TotalRows = 0 };
            yield break;
        }

        // 2. Fan out under a semaphore. Drain results as they complete via a Channel.
        var channel = Channel.CreateUnbounded<AdvocateSearchEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var sem = new SemaphoreSlim(MaxParallelDistricts);
        var totalRows = 0;
        var completed = 0;

        var producer = Task.Run(async () =>
        {
            var tasks = districts.Select(async d =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var evt = await SearchOneDistrictAsync(req, d.Code!, d.Name, ct);
                    var done = Interlocked.Increment(ref completed);
                    Interlocked.Add(ref totalRows, evt.Rows?.Count ?? 0);
                    evt.Type = "progress";
                    evt.CompletedDistricts = done;
                    evt.TotalDistricts = districts.Count;
                    await channel.Writer.WriteAsync(evt, ct);
                }
                catch (OperationCanceledException) { /* drained on shutdown */ }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Advocate search failed for district {District}", d.Code);
                    await channel.Writer.WriteAsync(new AdvocateSearchEvent
                    {
                        Type = "progress",
                        DistrictCode = d.Code,
                        DistrictName = d.Name,
                        Error = ex.Message,
                        CompletedDistricts = Interlocked.Increment(ref completed),
                        TotalDistricts = districts.Count
                    }, ct);
                }
                finally { sem.Release(); }
            });

            try { await Task.WhenAll(tasks); }
            finally { channel.Writer.Complete(); }
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;

        await producer;

        yield return new AdvocateSearchEvent { Type = "complete", TotalRows = totalRows };
    }

    // ─── Per-district call ───────────────────────────────────────────────────
    private async Task<AdvocateSearchEvent> SearchOneDistrictAsync(
        AdvocateSearchRequest req, string distCode, string? distName, CancellationToken ct)
    {
        // Enumerate complexes in the district, join njdg_est_codes.
        var (complexesNode, cErr) = await _ecourts.CallAsync("courtEstWebService.php", new()
        {
            ["action_code"] = "fillCourtComplex",
            ["state_code"] = req.StateCode,
            ["dist_code"] = distCode
        }, ct: ct);

        if (cErr != null)
        {
            return new AdvocateSearchEvent
            {
                DistrictCode = distCode,
                DistrictName = distName,
                Error = cErr,
                Rows = new()
            };
        }

        var courtCodes = complexesNode?["courtComplex"]?.AsArray()
            ?.Select(x => x?["njdg_est_code"]?.ToString())
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .Distinct()
            .ToList() ?? new();

        if (courtCodes.Count == 0)
        {
            return new AdvocateSearchEvent
            {
                DistrictCode = distCode,
                DistrictName = distName,
                Rows = new()
            };
        }

        var fields = BuildSearchFields(req, req.StateCode, distCode, string.Join(',', courtCodes));
        var (node, sErr) = await _ecourts.CallAsync("searchByAdvocateName.php", fields, ct: ct);

        return new AdvocateSearchEvent
        {
            DistrictCode = distCode,
            DistrictName = distName,
            Error = sErr,
            Rows = sErr == null ? Flatten(node) : new()
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, string> BuildSearchFields(
        AdvocateSearchRequest req, string stateCode, string distCode, string courtCodeArr)
    {
        var byBarcode = string.Equals(req.Mode, "barcode", StringComparison.OrdinalIgnoreCase);

        return new Dictionary<string, string>
        {
            ["state_code"] = stateCode,
            ["dist_code"] = distCode,
            ["court_code_arr"] = courtCodeArr,
            ["checkedSearchByRadioValue"] = byBarcode ? "2" : "1",
            ["advocateName"] = byBarcode ? "" : (req.AdvocateName ?? ""),
            ["barstatecode"] = byBarcode ? (req.BarStateCode ?? "") : "",
            ["barcode"] = byBarcode ? (req.BarCode ?? "") : "",
            ["pendingDisposed"] = string.IsNullOrWhiteSpace(req.PendingDisposed)
                ? "Pending"
                : req.PendingDisposed!,
            ["year"] = req.Year ?? "",
            ["date"] = req.Date ?? "",
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        };
    }

    /// <summary>
    /// The advocate-search response is keyed by establishment index. Flatten
    /// to a single list and copy <c>court_code</c> / <c>establishment_name</c>
    /// onto each row (same shape as case/filing-number search). Some builds
    /// nest <c>caseNos</c> as an object keyed by index rather than an array,
    /// so we tolerate both.
    /// </summary>
    private static List<JsonObject> Flatten(JsonNode? root)
    {
        var rows = new List<JsonObject>();
        if (root is not JsonObject obj) return rows;

        foreach (var (estKey, estNode) in obj)
        {
            if (estNode is not JsonObject est) continue;
            if (!int.TryParse(estKey, out _)) continue;

            var courtCode = est["court_code"]?.ToString();
            var establishment = est["establishment_name"]?.ToString();
            var advocate = est["advocateName"]?.ToString();
            var inner = est["caseNos"];

            IEnumerable<JsonNode?> src;
            if (inner is JsonArray arr) src = arr;
            else if (inner is JsonObject map) src = map.Select(kv => kv.Value);
            else continue;

            foreach (var item in src)
            {
                if (item is not JsonObject row) continue;
                var flat = (JsonObject)row.DeepClone();
                if (!string.IsNullOrEmpty(courtCode))      flat["court_code"]         ??= courtCode;
                if (!string.IsNullOrEmpty(establishment))  flat["establishment_name"] ??= establishment;
                if (!string.IsNullOrEmpty(advocate))       flat["advocateName"]       ??= advocate;
                rows.Add(flat);
            }
        }

        return rows;
    }
}

public class AdvocateSearchRequest
{
    public string StateCode { get; set; } = "";
    public string DistCode { get; set; } = "";
    public string? CourtCodeArr { get; set; }
    public string Mode { get; set; } = "name";        // "name" | "barcode"
    public string? AdvocateName { get; set; }
    public string? BarStateCode { get; set; }
    public string? BarCode { get; set; }
    public string? PendingDisposed { get; set; }      // Pending | Disposed | Both
    public string? Year { get; set; }
    public string? Date { get; set; }
}

public class AdvocateSearchEvent
{
    public string Type { get; set; } = "progress";    // start | progress | complete | error
    public string? StateCode { get; set; }
    public string? DistrictCode { get; set; }
    public string? DistrictName { get; set; }
    public int? CompletedDistricts { get; set; }
    public int? TotalDistricts { get; set; }
    public int? TotalRows { get; set; }
    public List<JsonObject>? Rows { get; set; }
    public string? Error { get; set; }
}
