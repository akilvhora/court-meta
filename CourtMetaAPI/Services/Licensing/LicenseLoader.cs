namespace CourtMetaAPI.Services.Licensing;

/// <summary>
/// Reads the on-disk license JWT placed by the installer at
/// <c>%ProgramData%\CourtMeta\license.jwt</c>. The installer wizard validates
/// the file before writing, so the API trusts the path; revalidation here is a
/// belt-and-braces safety net for hand-edited or rotated files.
/// </summary>
public sealed class LicenseLoader
{
    public string LicenseFilePath { get; }

    public LicenseLoader()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        LicenseFilePath = Path.Combine(programData, "CourtMeta", "license.jwt");
    }

    /// <summary>Returns the bare JWT text (for use as the Bearer token), or null if no license is installed.</summary>
    public string? TryReadInstalledLicense()
    {
        try
        {
            if (!File.Exists(LicenseFilePath)) return null;
            var text = File.ReadAllText(LicenseFilePath).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch
        {
            // Permission / IO errors fall through to "no license" — the
            // request will degrade to free behaviour. We log on first read in
            // the middleware so ops can spot this without spamming per-request.
            return null;
        }
    }
}
