namespace PesticideLabels.App.Pages;

/// <summary>Shows one saved label PDF inside the app.</summary>
public sealed class LabelViewerPage : ContentPage
{
    private readonly string _pdfPath;
    private readonly WebView _web = new();
    private readonly Label _status = new() { Padding = 16, TextColor = Palette.Muted, IsVisible = false };
    private bool _loaded;

    public LabelViewerPage(string pdfPath, string title)
    {
        _pdfPath = pdfPath;
        Title = title;

        var open = new ToolbarItem { Text = "Open / print" };
        open.Clicked += async (_, _) => await OpenExternallyAsync();
        ToolbarItems.Add(open);

        var grid = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        grid.Add(new PaperBanner(), 0, 0);
        grid.Add(_status, 0, 1);
        grid.Add(_web, 0, 2);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
            return;
        _loaded = true;
        try
        {
#if ANDROID
            // Android's WebView cannot display a PDF, so the pages are drawn to images first.
            _status.IsVisible = true;
            _status.Text = "Preparing the label…";
            var progress = new Progress<(int Page, int Total)>(p => _status.Text = $"Preparing page {p.Page} of {p.Total}…");
            var folder = Path.Combine(FileSystem.CacheDirectory, "pages", Path.GetFileNameWithoutExtension(_pdfPath));
            var index = await Task.Run(() => PdfPages.Render(_pdfPath, folder, progress));
            _status.IsVisible = false;
            _web.Source = new UrlWebViewSource { Url = "file://" + index };
#else
            _web.Source = new UrlWebViewSource { Url = new Uri(_pdfPath).AbsoluteUri };
            await Task.CompletedTask;
#endif
        }
        catch (Exception ex)
        {
            _web.IsVisible = false;
            _status.IsVisible = true;
            _status.TextColor = Palette.ErrorText;
            _status.Text = $"This label could not be displayed ({ex.Message}). Try \"Open / print\". {PaperCopy.OnFailure}";
        }
    }

    private async Task OpenExternallyAsync()
    {
        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest(Title, new ReadOnlyFile(_pdfPath, "application/pdf")));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not open the PDF", $"{ex.Message}\n\n{PaperCopy.OnFailure}", "OK");
        }
    }
}
