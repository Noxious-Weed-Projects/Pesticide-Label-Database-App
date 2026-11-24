using PesticideLabels.Core;

namespace PesticideLabels.App;

/// <summary>The few long-lived objects the pages share.</summary>
public static class AppServices
{
    private static readonly Lazy<HttpClient> LazyHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RushCountyPesticideLabels/0.2.0");
        return http;
    });

    private static readonly Lazy<PplsClient> LazyPpls = new(() => new PplsClient(LazyHttp.Value));

    private static readonly Lazy<LabelLibrary> LazyLibrary = new(() =>
        new LabelLibrary(Path.Combine(FileSystem.AppDataDirectory, "library"), LazyPpls.Value, LazyHttp.Value));

    public static PplsClient Ppls => LazyPpls.Value;
    public static LabelLibrary Library => LazyLibrary.Value;

    /// <summary>
    /// Frees storage on a tablet or phone: deletes the saved labels and Safety Data Sheets nobody
    /// has opened for a week, and the page images drawn for the viewer from them.
    /// </summary>
    public static Task RemoveUnusedDocumentsAsync() => Task.Run(() =>
    {
        try
        {
            Library.RemoveUnusedDocuments(LabelLibrary.UnusedDocumentLifetime);

            // The page images are larger than the PDF they were drawn from.
            var pages = Path.Combine(FileSystem.CacheDirectory, "pages");
            if (!Directory.Exists(pages))
                return;
            foreach (var folder in Directory.EnumerateDirectories(pages))
            {
                var pdf = Path.GetFileName(folder) + ".pdf";
                if (!Library.IsLabelSaved(pdf) && !Library.IsSdsSaved(pdf))
                    Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing staff can act on; the next launch tries again.
        }
    });
}

public static class Features
{
    /// <summary>
    /// Tablets and phones have little storage, so documents nobody has opened for a week are
    /// removed there. A PC keeps everything.
    /// </summary>
    public static readonly bool RemovesUnusedDocuments =
#if ANDROID
        true;
#else
        false;
#endif
}

public static class Palette
{
    public static readonly Color Green = Color.FromArgb("#2E6B34");
    public static readonly Color WarningBackground = Color.FromArgb("#FFE08A");
    public static readonly Color WarningText = Color.FromArgb("#3D2E00");
    public static readonly Color ErrorBackground = Color.FromArgb("#F8D7DA");
    public static readonly Color ErrorText = Color.FromArgb("#58151C");
    public static readonly Color Muted = Color.FromArgb("#6B7280");
}

public static class PaperCopy
{
    public const string Short = "Keep paper copies of every label with you. This app may not work in the field.";

    public const string OnFailure = "Use your paper copy of the label.";

    public const string Removal = "To save storage, a label or Safety Data Sheet that has not been opened for 7 days is removed from this device and needs the internet to open again.";
}
