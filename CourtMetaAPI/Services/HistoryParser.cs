using System.Text.Json;
using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services;

/// <summary>
/// Parses the <c>history</c> payload returned by <c>caseHistoryWebService.php</c> /
/// <c>filingCaseHistory.php</c> into a structured <see cref="CaseBundle"/>.
///
/// The upstream payload is heterogeneous: depending on the court / case type, the
/// <c>history</c> field can be:
///   - a JSON object with named sections (the common case),
///   - a JSON object with HTML fragments inside specific keys (e.g. caseOrders),
///   - or, occasionally, a single rendered HTML string.
///
/// The parser is intentionally tolerant — it extracts what it recognises and
/// preserves anything it doesn't under <see cref="CaseBundle.Raw"/> so callers can
/// still drill in. Never throws on shape mismatches; missing fields surface as null.
/// </summary>
public class HistoryParser
{
    private readonly ILogger<HistoryParser> _logger;

    public HistoryParser(ILogger<HistoryParser> logger)
    {
        _logger = logger;
    }

    public CaseBundle Parse(string cino, string caseType, JsonNode? history)
    {
        var bundle = new CaseBundle
        {
            Cino = cino,
            CaseType = caseType,
            Raw = history?.DeepClone()
        };

        if (history is null)
            return bundle;

        // history-as-string: keep it as html. Caller can render or post-process.
        if (history is JsonValue v && v.TryGetValue<string>(out var asText))
        {
            bundle.HistoryHtml = asText;
            return bundle;
        }

        if (history is not JsonObject obj)
            return bundle;

        bundle.CaseInfo = ExtractCaseInfo(obj);
        bundle.Parties = ExtractParties(obj);
        bundle.Advocates = ExtractAdvocates(obj);
        bundle.Court = ExtractCourt(obj);
        bundle.Filing = ExtractFiling(obj);
        bundle.Hearings = ExtractList(obj, "hearing", new[] { "date", "purpose", "judge" }, BuildHearing);
        bundle.Transfers = ExtractList(obj, "transfer", new[] { "from", "to", "date", "reason" }, BuildTransfer);
        bundle.Processes = ExtractList(obj, "process", new[] { "issue", "process", "date" }, BuildProcess);
        bundle.Orders = ExtractList(obj, "order", new[] { "order", "date", "judge" }, BuildOrder);

        return bundle;
    }

    // ─── Section extractors ───────────────────────────────────────────────────

    private static CaseInfo ExtractCaseInfo(JsonObject h) => new()
    {
        CaseNumber = Str(h, "case_no", "case_number", "case_no2"),
        RegistrationNumber = Str(h, "reg_no", "registration_no"),
        RegistrationYear = Str(h, "reg_year", "registration_year"),
        CnrType = Str(h, "type_name", "ltype_name"),
        Status = Str(h, "status", "case_status"),
        Stage = Str(h, "stage_of_case", "stage"),
        NextHearingDate = Str(h, "date_next_list", "next_date"),
        LastHearingDate = Str(h, "date_last_list", "last_date"),
        DateOfDecision = Str(h, "date_of_decision", "decision_date"),
        Purpose = Str(h, "purpose_name", "purpose"),
        Disposal = Str(h, "disp_name", "disposal_nature")
    };

    private static Parties ExtractParties(JsonObject h) => new()
    {
        Petitioners = SplitNames(Str(h, "pet_name", "petitioner_name", "petname")),
        Respondents = SplitNames(Str(h, "res_name", "respondent_name", "resname")),
        PetitionerAddress = Str(h, "pet_adr", "petitioner_address"),
        RespondentAddress = Str(h, "res_adr", "respondent_address")
    };

    private static Advocates ExtractAdvocates(JsonObject h) => new()
    {
        ForPetitioner = SplitNames(Str(h, "pet_adv", "pet_advocate", "petitioner_advocate")),
        ForRespondent = SplitNames(Str(h, "res_adv", "res_advocate", "respondent_advocate"))
    };

    private static CourtInfo ExtractCourt(JsonObject h) => new()
    {
        CourtName = Str(h, "court_name", "lcourt_name", "establishment_name"),
        CourtNumber = Str(h, "court_no", "court_number"),
        JudgeName = Str(h, "desgname", "judge_name", "ldesgname"),
        DistrictName = Str(h, "district_name", "ldistrict_name"),
        StateName = Str(h, "state_name", "lstate_name")
    };

    private static FilingInfo ExtractFiling(JsonObject h) => new()
    {
        FilingNumber = Str(h, "fil_no", "filing_no", "filing_number"),
        FilingYear = Str(h, "fil_year", "filing_year"),
        FilingDate = Str(h, "date_of_filing", "filing_date"),
        RegistrationDate = Str(h, "date_of_registration", "registration_date")
    };

    // ─── List/array extraction ────────────────────────────────────────────────

    /// <summary>
    /// Walk the history object looking for an array under any key matching
    /// <paramref name="keyHint"/> (case-insensitive substring) whose elements look
    /// like rows for the section (i.e. expose at least one of <paramref name="rowKeyHints"/>).
    /// </summary>
    private List<T> ExtractList<T>(
        JsonObject root,
        string keyHint,
        string[] rowKeyHints,
        Func<JsonObject, T> build) where T : class
    {
        var result = new List<T>();
        var matches = FindArrays(root, keyHint, rowKeyHints, maxDepth: 4);

        foreach (var arr in matches)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject row)
                {
                    try { result.Add(build(row)); }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to build row for {Key}", keyHint);
                    }
                }
            }
        }
        return result;
    }

    private static IEnumerable<JsonArray> FindArrays(
        JsonNode? node, string keyHint, string[] rowKeyHints, int maxDepth, int depth = 0)
    {
        if (node is null || depth > maxDepth) yield break;

        if (node is JsonObject obj)
        {
            foreach (var (key, child) in obj)
            {
                if (child is null) continue;

                if (child is JsonArray arr
                    && key.Contains(keyHint, StringComparison.OrdinalIgnoreCase)
                    && ArrayLooksLikeRows(arr, rowKeyHints))
                {
                    yield return arr;
                    continue;
                }

                foreach (var inner in FindArrays(child, keyHint, rowKeyHints, maxDepth, depth + 1))
                    yield return inner;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                foreach (var inner in FindArrays(item, keyHint, rowKeyHints, maxDepth, depth + 1))
                    yield return inner;
        }
    }

    private static bool ArrayLooksLikeRows(JsonArray arr, string[] rowKeyHints)
    {
        if (arr.Count == 0) return true;
        var first = arr.FirstOrDefault(a => a is JsonObject) as JsonObject;
        if (first is null) return false;
        return rowKeyHints.Any(hint =>
            first.Any(kvp => kvp.Key.Contains(hint, StringComparison.OrdinalIgnoreCase)));
    }

    // ─── Row builders ─────────────────────────────────────────────────────────

    private static Hearing BuildHearing(JsonObject row) => new()
    {
        Date = FuzzyStr(row, regex: "^(?!next).*(date|hearing)"),
        Purpose = FuzzyStr(row, regex: "^(?!next).*purpose"),
        Judge = FuzzyStr(row, regex: "judge"),
        Business = FuzzyStr(row, regex: "business"),
        NextDate = FuzzyStr(row, regex: "next.*date"),
        NextPurpose = FuzzyStr(row, regex: "next.*purpose"),
        CourtNo = FuzzyStr(row, regex: "court.*no"),
        DisposalFlag = FuzzyStr(row, regex: "disposal_flag|disposal"),
        Raw = row.DeepClone()
    };

    private static Transfer BuildTransfer(JsonObject row) => new()
    {
        FromCourt = FuzzyStr(row, regex: "from"),
        ToCourt = FuzzyStr(row, regex: "to(?!tal)"),
        Date = FuzzyStr(row, regex: "date"),
        Reason = FuzzyStr(row, regex: "reason|remark"),
        Raw = row.DeepClone()
    };

    private static ProcessItem BuildProcess(JsonObject row) => new()
    {
        ProcessId = FuzzyStr(row, regex: "process_?id|proc_id"),
        IssueDate = FuzzyStr(row, regex: "issue.*date|date_of_issue"),
        Process = FuzzyStr(row, regex: "process(?!_id|_name_l)|process_name"),
        Party = FuzzyStr(row, regex: "party|name"),
        Raw = row.DeepClone()
    };

    private static OrderItem BuildOrder(JsonObject row) => new()
    {
        OrderNumber = FuzzyStr(row, regex: "order_?no|order_id|orderyr|order_number"),
        OrderDate = FuzzyStr(row, regex: "order.*date|date.*order"),
        Judge = FuzzyStr(row, regex: "judge"),
        OrderDetails = FuzzyStr(row, regex: "order_?details|order_text|details"),
        PdfLink = FuzzyStr(row, regex: "pdf|order_link|file"),
        Raw = row.DeepClone()
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string? Str(JsonObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (o.TryGetPropertyValue(k, out var n) && n is not null)
            {
                var s = n.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        }
        return null;
    }

    private static string? FuzzyStr(JsonObject o, string regex)
    {
        var rx = new System.Text.RegularExpressions.Regex(
            regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var (key, value) in o)
        {
            if (rx.IsMatch(key) && value is not null)
            {
                var s = value.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        }
        return null;
    }

    private static List<string> SplitNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw
            .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')', ' '))
            .Where(s => s.Length > 0)
            .ToList();
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

public class CaseBundle
{
    public string Cino { get; set; } = "";
    public string CaseType { get; set; } = "";          // "case" | "filing"
    public CaseInfo? CaseInfo { get; set; }
    public Parties? Parties { get; set; }
    public Advocates? Advocates { get; set; }
    public CourtInfo? Court { get; set; }
    public FilingInfo? Filing { get; set; }
    public List<Hearing> Hearings { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
    public List<ProcessItem> Processes { get; set; } = new();
    public List<OrderItem> Orders { get; set; } = new();
    public string? HistoryHtml { get; set; }            // when upstream returned a string blob
    public JsonNode? Raw { get; set; }
}

public class CaseInfo
{
    public string? CaseNumber { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? RegistrationYear { get; set; }
    public string? CnrType { get; set; }
    public string? Status { get; set; }
    public string? Stage { get; set; }
    public string? NextHearingDate { get; set; }
    public string? LastHearingDate { get; set; }
    public string? DateOfDecision { get; set; }
    public string? Purpose { get; set; }
    public string? Disposal { get; set; }
}

public class Parties
{
    public List<string> Petitioners { get; set; } = new();
    public List<string> Respondents { get; set; } = new();
    public string? PetitionerAddress { get; set; }
    public string? RespondentAddress { get; set; }
}

public class Advocates
{
    public List<string> ForPetitioner { get; set; } = new();
    public List<string> ForRespondent { get; set; } = new();
}

public class CourtInfo
{
    public string? CourtName { get; set; }
    public string? CourtNumber { get; set; }
    public string? JudgeName { get; set; }
    public string? DistrictName { get; set; }
    public string? StateName { get; set; }
}

public class FilingInfo
{
    public string? FilingNumber { get; set; }
    public string? FilingYear { get; set; }
    public string? FilingDate { get; set; }
    public string? RegistrationDate { get; set; }
}

public class Hearing
{
    public string? Date { get; set; }
    public string? Purpose { get; set; }
    public string? Judge { get; set; }
    public string? Business { get; set; }
    public string? NextDate { get; set; }
    public string? NextPurpose { get; set; }
    public string? CourtNo { get; set; }
    public string? DisposalFlag { get; set; }
    public JsonNode? Raw { get; set; }
}

public class Transfer
{
    public string? FromCourt { get; set; }
    public string? ToCourt { get; set; }
    public string? Date { get; set; }
    public string? Reason { get; set; }
    public JsonNode? Raw { get; set; }
}

public class ProcessItem
{
    public string? ProcessId { get; set; }
    public string? IssueDate { get; set; }
    public string? Process { get; set; }
    public string? Party { get; set; }
    public JsonNode? Raw { get; set; }
}

public class OrderItem
{
    public string? OrderNumber { get; set; }
    public string? OrderDate { get; set; }
    public string? Judge { get; set; }
    public string? OrderDetails { get; set; }
    public string? PdfLink { get; set; }
    public JsonNode? Raw { get; set; }
}
