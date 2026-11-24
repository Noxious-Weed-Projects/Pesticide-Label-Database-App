using PesticideLabels.Core;

namespace PesticideLabels.App;

/// <summary>The few long-lived objects the pages share.</summary>
public static class AppServices
{
    private static readonly Lazy<HttpClient> LazyHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RushCountyPesticideLabels/0.1");
        return http;
    });

    private static readonly Lazy<PplsClient> LazyPpls = new(() => new PplsClient(LazyHttp.Value));

    private static readonly Lazy<LabelLibrary> LazyLibrary = new(() =>
        new LabelLibrary(Path.Combine(FileSystem.AppDataDirectory, "library"), LazyPpls.Value, LazyHttp.Value));

    public static PplsClient Ppls => LazyPpls.Value;
    public static LabelLibrary Library => LazyLibrary.Value;
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
}
