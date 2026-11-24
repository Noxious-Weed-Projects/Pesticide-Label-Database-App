using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PesticideLabels.Core;

/// <summary>One product in the CDMS Label Database.</summary>
public sealed class CdmsProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Manufacturer { get; set; }

    /// <summary>EPA Reg. No. as CDMS shows it; "EXEMPT" or similar for unregistered products.</summary>
    [JsonPropertyName("EPA")] public string? EpaRegNo { get; set; }

    public string? CommonName { get; set; }
    public bool IsUs { get; set; }
    public bool IsCanada { get; set; }
}

/// <summary>One document CDMS holds for a product: a label, a supplemental label or a Safety Data Sheet.</summary>
public sealed class CdmsDocument
{
    public string FileName { get; set; } = "";
    public string? Description { get; set; }
    public string? DocType { get; set; }
    public int DocId { get; set; }
    public List<string> LanguageCodes { get; set; } = [];

    [JsonIgnore]
    public bool IsSafetyDataSheet =>
        string.Equals(DocType?.Trim(), "SDS", StringComparison.OrdinalIgnoreCase) && CdmsClient.IsValidFileName(FileName);
}

public sealed class CdmsList<T>
{
    public List<T> Lst { get; set; } = [];
}

/// <summary>
/// Client for the CDMS Label Database (https://www.cdms.net/labelssds/home/), used for
/// Safety Data Sheets. It calls the same addresses the CDMS web page calls.
/// </summary>
public sealed partial class CdmsClient(HttpClient http)
{
    public const string SiteBase = "https://www.cdms.net/LabelsSDS/Home/";
    public const string DocumentBase = "https://www.cdms.net/ldat/";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-]*\.pdf$", RegexOptions.IgnoreCase)]
    private static partial Regex FilePattern();

    public static bool IsValidFileName(string? fileName) =>
        !string.IsNullOrEmpty(fileName) && FilePattern().IsMatch(fileName);

    public static Uri SearchUri(string nameFragment) =>
        new($"{SiteBase}ProductList?namePat={Uri.EscapeDataString(nameFragment.Trim())}");

    public static Uri DocumentListUri(int productId) => new($"{SiteBase}DocumentList?productId={productId}");

    public static Uri DocumentUri(string fileName)
    {
        if (!IsValidFileName(fileName))
            throw new ArgumentException("Not a CDMS document file name.", nameof(fileName));
        return new Uri(DocumentBase + fileName);
    }

    /// <summary>
    /// Search texts to try for a county product, most specific first. CDMS matches on the
    /// brand name alone, so "Corteva High Noon" must also be tried as "High Noon". Later texts
    /// are only used when the earlier ones find nothing.
    /// </summary>
    public static IReadOnlyList<string> SearchTerms(CountyProduct product)
    {
        var terms = new List<string>();
        void Add(string? text)
        {
            text = text?.Trim();
            if (text is { Length: >= 3 } && !terms.Contains(text, StringComparer.OrdinalIgnoreCase))
                terms.Add(text);
        }

        var name = product.Name.Trim();
        Add(name);
        // The maker's name can be one or two words ("Corteva Agriscience Piper EZ").
        for (var dropped = 0; dropped < 2 && name.IndexOf(' ') is > 0 and var space; dropped++)
        {
            name = name[(space + 1)..].TrimStart();
            Add(name);
        }
        Add(product.SearchHint);
        return terms;
    }

    /// <summary>
    /// The one US product whose EPA Reg. No. equals the county product's number (or its parent
    /// registration), or null when there is no such product or more than one.
    /// </summary>
    public static CdmsProduct? MatchByRegNo(IEnumerable<CdmsProduct> hits, string? regNo)
    {
        if (!PplsClient.TryNormalizeRegNo(regNo, out var wanted))
            return null;
        var us = hits.Where(h => h.IsUs).ToList();
        var exact = us.Where(h => SameRegNo(h.EpaRegNo, wanted)).ToList();
        if (exact.Count == 0 && PplsClient.ParentRegNo(wanted) is { } parent)
            exact = [.. us.Where(h => SameRegNo(h.EpaRegNo, parent))];
        return exact.Count == 1 ? exact[0] : null;
    }

    private static bool SameRegNo(string? cdms, string wanted) =>
        PplsClient.TryNormalizeRegNo(cdms, out var normalized) && normalized == wanted;

    /// <summary>Products whose brand name contains the text. US products come first.</summary>
    public async Task<IReadOnlyList<CdmsProduct>> SearchAsync(string nameFragment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nameFragment))
            return [];
        await using var stream = await http.GetStreamAsync(SearchUri(nameFragment), ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.CdmsListCdmsProduct, ct).ConfigureAwait(false);
        return [.. (result?.Lst ?? []).Where(p => p.Id > 0).OrderByDescending(p => p.IsUs)];
    }

    /// <summary>Every document CDMS holds for a product.</summary>
    public async Task<IReadOnlyList<CdmsDocument>> GetDocumentsAsync(int productId, CancellationToken ct = default)
    {
        await using var stream = await http.GetStreamAsync(DocumentListUri(productId), ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.CdmsListCdmsDocument, ct).ConfigureAwait(false);
        return result?.Lst ?? [];
    }
}
