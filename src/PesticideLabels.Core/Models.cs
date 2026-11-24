using System.Globalization;
using System.Text.Json.Serialization;

namespace PesticideLabels.Core;

/// <summary>Where the label for a county product comes from.</summary>
public enum LabelSource
{
    /// <summary>EPA-registered pesticide; label comes from PPLS.</summary>
    Ppls,

    /// <summary>Adjuvant, dye or other product with no EPA label in PPLS.</summary>
    NoEpaLabel,
}

/// <summary>One product the Noxious Weed Department uses or sells.</summary>
public sealed class CountyProduct
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public LabelSource Source { get; set; } = LabelSource.Ppls;

    /// <summary>EPA registration number as printed on the container, e.g. 62719-519.</summary>
    public string? EpaRegNo { get; set; }

    /// <summary>True once staff have checked the number against the actual container.</summary>
    public bool Confirmed { get; set; }

    /// <summary>Text to pre-fill when searching PPLS by name.</summary>
    public string? SearchHint { get; set; }

    public string? Note { get; set; }

    /// <summary>The product in the CDMS Label Database whose Safety Data Sheets are shown, once matched.</summary>
    public int? CdmsProductId { get; set; }

    [JsonIgnore]
    public bool HasRegNo => Source == LabelSource.Ppls && !string.IsNullOrWhiteSpace(EpaRegNo);
}

public sealed class PplsItems<T>
{
    [JsonPropertyName("items")] public List<T> Items { get; set; } = [];
}

/// <summary>One row from the PPLS product-name search.</summary>
public sealed class PplsSearchHit
{
    [JsonPropertyName("eparegno")] public string EpaRegNo { get; set; } = "";
    [JsonPropertyName("productname")] public string ProductName { get; set; } = "";
    [JsonPropertyName("product_status")] public string? ProductStatus { get; set; }
    [JsonPropertyName("product_status_date")] public string? ProductStatusDate { get; set; }
    [JsonPropertyName("altrntbrndnames")] public string? AlternateBrandNames { get; set; }

    [JsonIgnore] public bool IsActive => string.Equals(ProductStatus, "Active", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Product details from PPLS for one EPA registration number.</summary>
public sealed class PplsProduct
{
    [JsonPropertyName("eparegno")] public string EpaRegNo { get; set; } = "";
    [JsonPropertyName("productname")] public string ProductName { get; set; } = "";
    [JsonPropertyName("product_status")] public string? ProductStatus { get; set; }
    [JsonPropertyName("product_status_date")] public string? ProductStatusDate { get; set; }
    [JsonPropertyName("cancel_flag")] public string? CancelFlag { get; set; }
    [JsonPropertyName("signal_word")] public string? SignalWord { get; set; }
    [JsonPropertyName("rup_yn")] public string? RestrictedUse { get; set; }
    [JsonPropertyName("companyinfo")] public List<PplsCompany> Companies { get; set; } = [];
    [JsonPropertyName("formulations")] public List<PplsFormulation> Formulations { get; set; } = [];
    [JsonPropertyName("active_ingredients")] public List<PplsActiveIngredient> ActiveIngredients { get; set; } = [];
    [JsonPropertyName("pdffiles")] public List<PplsLabelFile> PdfFiles { get; set; } = [];
    [JsonPropertyName("altbrandnames")] public List<PplsAltBrandName> AltBrandNames { get; set; } = [];

    [JsonIgnore] public bool IsRestrictedUse => string.Equals(RestrictedUse, "Yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>Accepted label PDFs, newest first.</summary>
    [JsonIgnore]
    public IReadOnlyList<PplsLabelFile> LabelsNewestFirst =>
        [.. PdfFiles.Where(f => PplsClient.IsValidLabelFileName(f.FileName))
            .OrderByDescending(f => f.AcceptedDate ?? DateTime.MinValue)];
}

public sealed class PplsCompany
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
}

public sealed class PplsFormulation
{
    [JsonPropertyName("formulation")] public string? Formulation { get; set; }
}

public sealed class PplsAltBrandName
{
    [JsonPropertyName("altbrandname")] public string? Name { get; set; }
}

public sealed class PplsActiveIngredient
{
    [JsonPropertyName("active_ing")] public string? Name { get; set; }
    [JsonPropertyName("active_ing_percent")] public double? Percent { get; set; }
}

public sealed class PplsLabelFile
{
    [JsonPropertyName("pdffile")] public string FileName { get; set; } = "";
    [JsonPropertyName("pdffile_accepted_date")] public string? AcceptedDateText { get; set; }

    /// <summary>Date EPA accepted this label, from the API text or the file name suffix.</summary>
    [JsonIgnore]
    public DateTime? AcceptedDate
    {
        get
        {
            if (DateTime.TryParse(AcceptedDateText, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var d))
                return d;
            var stem = Path.GetFileNameWithoutExtension(FileName);
            var dash = stem.LastIndexOf('-');
            if (dash >= 0 && DateTime.TryParseExact(stem[(dash + 1)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
            return null;
        }
    }
}

/// <summary>A CDMS product and its Safety Data Sheets as saved on the device.</summary>
public sealed class CachedSds
{
    public DateTimeOffset FetchedAt { get; set; }
    public CdmsProduct? Product { get; set; }
    public List<CdmsDocument> Sheets { get; set; } = [];
}

/// <summary>PPLS details as saved on the device, with the time they were fetched.</summary>
public sealed class CachedProduct
{
    public DateTimeOffset FetchedAt { get; set; }
    public PplsProduct? Product { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PplsItems<PplsSearchHit>))]
[JsonSerializable(typeof(PplsItems<PplsProduct>))]
[JsonSerializable(typeof(CachedProduct))]
[JsonSerializable(typeof(CachedSds))]
[JsonSerializable(typeof(CdmsList<CdmsProduct>))]
[JsonSerializable(typeof(CdmsList<CdmsDocument>))]
[JsonSerializable(typeof(List<CountyProduct>))]
public sealed partial class CoreJson : JsonSerializerContext;
