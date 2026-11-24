namespace PesticideLabels.Core;

/// <summary>
/// The department's chemical list (from "Pesitcidle List.xlsx", November 2026).
/// The spreadsheet has names only. Registration numbers here are name matches from PPLS
/// and are unconfirmed until staff check them against the container.
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
        Ppls("Alligare 2,4-D", null, "ALLIGARE 2,4-D"),
        Ppls("AquaNeat", null, "AQUANEAT"),
        NoLabel("Bubble Blaster"),
        Ppls("Diablo", "228-379"),
        Ppls("Drexel Diuron 4L", null, "DIURON 4L"),
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
        Ppls("Loveland MAD DOG", null, "MAD DOG"),
        Ppls("Alligare MSM 60", "81927-7"),
        Ppls("Alligare Panoramic 2SL", "66222-141-81927"),
        NoLabel("MARC Photo Blue Dye"),
        NoLabel("Blue Wing"),
        Ppls("Alligare Picloram 22K", "81927-18"),
        Ppls("Alligare Triumph 22K", "81927-18"),
        Ppls("Loveland Rifle", null, "RIFLE"),
        NoLabel("Drexel Surf-AC 910"),
        Ppls("Corteva Tordon 22K", "62719-6"),
        Ppls("Corteva Agriscience Piper EZ", "59639-237"),
        Ppls("Valent Outrider", "59639-223"),
        NoLabel("Loveland Choice Weathermaster"),
        NoLabel("Loveland LI 700"),
        NoLabel("Loveland Reign"),
    ];

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
