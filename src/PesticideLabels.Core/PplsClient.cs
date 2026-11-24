using System.Text.Json;
using System.Text.RegularExpressions;

namespace PesticideLabels.Core;

/// <summary>
/// Client for the EPA Pesticide Product Label System (PPLS) API.
/// https://www.epa.gov/pesticide-labels/pesticide-product-label-system-ppls-application-program-interface-api
/// </summary>
public sealed partial class PplsClient(HttpClient http)
{
    public const string ApiBase = "https://ordspub.epa.gov/ords/pesticides/cswu/";
    public const string LabelPdfBase = "https://www3.epa.gov/pesticides/chem_search/ppls/";

    [GeneratedRegex(@"^\d{1,6}-\d{1,5}(-\d{1,6})?$")]
    private static partial Regex RegNoPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-]*\.pdf$", RegexOptions.IgnoreCase)]
    private static partial Regex LabelFilePattern();

    /// <summary>Trims and checks an EPA registration number (company-product or company-product-distributor).</summary>
    public static bool TryNormalizeRegNo(string? text, out string regNo)
    {
        regNo = (text ?? "").Trim().Replace(" ", "").Replace('–', '-');
        return RegNoPattern().IsMatch(regNo);
    }

    /// <summary>
    /// A distributor product (three-part number) is sold under the parent registration's label,
    /// so PPLS files it under the first two parts. Returns null for a two-part number.
    /// </summary>
    public static string? ParentRegNo(string regNo)
    {
        var parts = regNo.Split('-');
        return parts.Length == 3 ? $"{parts[0]}-{parts[1]}" : null;
    }

    public static bool IsValidLabelFileName(string? fileName) =>
        !string.IsNullOrEmpty(fileName) && LabelFilePattern().IsMatch(fileName);

    public static Uri LabelPdfUri(string fileName)
    {
        if (!IsValidLabelFileName(fileName))
            throw new ArgumentException("Not a PPLS label file name.", nameof(fileName));
        return new Uri(LabelPdfBase + fileName);
    }

    public static Uri SearchUri(string nameFragment)
    {
        // The name is a path segment, so a slash would change the route.
        var term = nameFragment.Trim().Replace('/', ' ').Replace('\\', ' ');
        return new Uri($"{ApiBase}ProductSearch/partialprodsearch/v2/riname/{Uri.EscapeDataString(term)}");
    }

    public static Uri ProductUri(string regNo) => new($"{ApiBase}ppls/{Uri.EscapeDataString(regNo)}");

    /// <summary>Finds products whose name contains the text. Active federal registrations come first.</summary>
    public async Task<IReadOnlyList<PplsSearchHit>> SearchByNameAsync(string nameFragment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nameFragment))
            return [];
        await using var stream = await http.GetStreamAsync(SearchUri(nameFragment), ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.PplsItemsPplsSearchHit, ct).ConfigureAwait(false);
        var hits = result?.Items ?? [];
        return [.. hits
            .Where(h => h.EpaRegNo.Length > 0)
            .GroupBy(h => (h.EpaRegNo, h.ProductName.Trim().ToUpperInvariant()))
            .Select(g => g.First())
            .OrderByDescending(h => h.IsActive)
            .ThenByDescending(h => char.IsDigit(h.EpaRegNo[0]))
            .ThenBy(h => h.ProductName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Gets product details and the label list. A three-part distributor number that PPLS
    /// does not list directly is looked up under its parent registration.
    /// </summary>
    public async Task<PplsProduct?> GetProductAsync(string regNo, CancellationToken ct = default)
    {
        if (!TryNormalizeRegNo(regNo, out var normalized))
            throw new ArgumentException("Not an EPA registration number.", nameof(regNo));

        var product = await FetchAsync(normalized, ct).ConfigureAwait(false);
        if (product is null && ParentRegNo(normalized) is { } parent)
            product = await FetchAsync(parent, ct).ConfigureAwait(false);
        return product;
    }

    private async Task<PplsProduct?> FetchAsync(string regNo, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(ProductUri(regNo), ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.PplsItemsPplsProduct, ct).ConfigureAwait(false);
        return result?.Items.FirstOrDefault();
    }
}
