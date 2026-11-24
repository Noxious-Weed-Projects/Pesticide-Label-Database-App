namespace PesticideLabels.Core;

/// <summary>
/// The department's chemical list (from "Pesitcidle List.xlsx", November 2026).
/// The spreadsheet gives an EPA number for a few products; the other registration numbers
/// here are name matches from PPLS. All are unconfirmed until staff check them against the container.
/// </summary>
public static class SeedProducts
{
    private const string NoLabelNote =
        "Not found in EPA's label system. Adjuvants, surfactants and dyes are usually not EPA-registered. " +
        "Use the paper label and SDS from the supplier.";

    public static List<CountyProduct> Create() =>
    [
        Ppls("Drexel De-Amine 4", "19713-650"),
        Ppls("Sharda Picloram 24% SL", "83529-126"),
        Ppls("Alligare 2,4-D LV 6", "81927-39"),
        Ppls("Alligare 2,4-D", "81927-38"),
        Ppls("AquaNeat", "228-365"),
        NoLabel("Bubble Blaster"),
        Ppls("Diablo", "228-379"),
        // The spreadsheet has 19713-36 on the Diablo row above. The department confirmed it is
        // Diuron 4L's number (EPA registers it as Drexel Diuron 4L); Diablo keeps Nufarm's 228-379.
        Ppls("Drexel Diuron 4L", "19713-36"),
        Ppls("Alligare Glyphosate 4 Plus", "81927-9"),
        Ppls("GLY Star Plus", "42750-61"),
        Ppls("Corteva High Noon", "62719-755"),
        NoLabel("Drexel Hot Mes"),
        Ppls("Drexel Imitator Aquatic", "19713-623"),
        NoLabel("Loveland Liberate"),
        Ppls("Loveland Makaze", "34704-890"),
        Ppls("Corteva Milestone", "62719-519"),
        Ppls("Alligare Mojave 70 EG", "81927-25"),
        Ppls("Nufarm Imazuron", "228-654"),
        Ppls("Loveland MAD DOG", "34704-889"),
        Ppls("Alligare MSM 60", "81927-7"),
        Ppls("Alligare Panoramic 2SL", "66222-141-81927"),
        NoLabel("MARC Photo Blue Dye"),
        NoLabel("Blue Wing"),
        Ppls("Alligare Picloram 22K", "81927-18"),
        Ppls("Alligare Triumph 22K", "81927-18"),
        Ppls("Loveland Rifle", "34704-861"),
        NoLabel("Drexel Surf-AC 910"),
        Ppls("Corteva Tordon 22K", "62719-6"),
        Ppls("Corteva Agriscience Piper EZ", "59639-237"),
        Ppls("Valent Outrider", "59639-223"),
        NoLabel("Loveland Choice Weathermaster"),
        NoLabel("Loveland LI 700"),
        NoLabel("Loveland Reign"),
        Ppls("Cornbelt 4 Lb. Amine", "11773-2"),
    ];

    /// <summary>
    /// Products that had no built-in EPA number until the county list supplied one
    /// (v0.2.1, and Cornbelt in v0.2.2).
    /// </summary>
    private static readonly string[] NumbersAddedToList =
        ["alligare-2-4-d", "aquaneat", "drexel-diuron-4l", "loveland-mad-dog", "loveland-rifle", "cornbelt-4-lb--amine"];

    /// <summary>
    /// Brings a list saved by an earlier version up to the current county list: adds products
    /// that are new to the list and fills in the EPA numbers the list gained. A number staff
    /// entered or confirmed on the device is never replaced. Returns true if anything changed.
    /// </summary>
    public static bool ApplyListUpdates(List<CountyProduct> saved)
    {
        var changed = false;
        var seed = Create();
        foreach (var id in NumbersAddedToList)
        {
            var product = saved.FirstOrDefault(p => p.Id == id);
            if (product is { Source: LabelSource.Ppls, Confirmed: false } && string.IsNullOrWhiteSpace(product.EpaRegNo))
            {
                product.EpaRegNo = seed.First(p => p.Id == id).EpaRegNo;
                changed = true;
            }
        }
        foreach (var product in seed.Where(p => saved.All(s => s.Id != p.Id)))
        {
            saved.Add(product);
            changed = true;
        }
        return changed;
    }

    public static string MakeId(string name) =>
        new string([.. name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-');

    private static CountyProduct Ppls(string name, string? regNo, string? hint = null) => new()
    {
        Id = MakeId(name),
        Name = name,
        Source = LabelSource.Ppls,
        EpaRegNo = regNo,
        SearchHint = hint ?? name,
    };

    private static CountyProduct NoLabel(string name) => new()
    {
        Id = MakeId(name),
        Name = name,
        Source = LabelSource.NoEpaLabel,
        SearchHint = name,
        Note = NoLabelNote,
    };
}
