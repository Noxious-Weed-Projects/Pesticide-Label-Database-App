using PesticideLabels.Core;

namespace PesticideLabels.App.Pages;

public sealed record ProductRow(CountyProduct Product, string Name, string Subtitle, Color SubtitleColor);

/// <summary>The county product list, with search and "download all labels".</summary>
public sealed class ProductsPage : ContentPage
{
    private readonly SearchBar _search = new() { Placeholder = "Search products or EPA Reg. No." };
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None };
    private readonly Label _status = new() { FontSize = 13, TextColor = Palette.Muted, Padding = new Thickness(14, 6) };
    private readonly Button _download = new() { Text = "Download all labels for offline use", Margin = new Thickness(14, 4, 14, 10) };
    private List<CountyProduct> _products = [];
    private bool _downloading;

    public ProductsPage()
    {
        Title = "Rush County Pesticide Labels";

        _list.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { FontSize = 18, FontFamily = "OpenSansSemibold" };
            name.SetBinding(Label.TextProperty, Binding.Create(static (ProductRow r) => r.Name));
            var subtitle = new Label { FontSize = 14 };
            subtitle.SetBinding(Label.TextProperty, Binding.Create(static (ProductRow r) => r.Subtitle));
            subtitle.SetBinding(Label.TextColorProperty, Binding.Create(static (ProductRow r) => r.SubtitleColor));

            var row = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = 2, Children = { name, subtitle } };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (sender, _) =>
            {
                if ((sender as BindableObject)?.BindingContext is ProductRow selected)
                    await Navigation.PushAsync(new ProductDetailPage(selected.Product.Id));
            };
            row.GestureRecognizers.Add(tap);
            return row;
        });
        _list.EmptyView = new Label { Text = "No products match.", Padding = 24, TextColor = Palette.Muted };

        _search.TextChanged += (_, _) => ApplyFilter();
        _download.Clicked += async (_, _) => await DownloadAllAsync();

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(new PaperBanner(), 0, 0);
        grid.Add(_search, 0, 1);
        grid.Add(_list, 0, 2);
        grid.Add(_status, 0, 3);
        grid.Add(_download, 0, 4);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _products = await AppServices.Library.LoadProductsAsync();
        ApplyFilter();
        if (!_downloading)
            ShowCounts();
    }

    private void ShowCounts()
    {
        var withNumber = _products.Count(p => p.HasRegNo);
        var needNumber = _products.Count(p => p.Source == LabelSource.Ppls && !p.HasRegNo);
        var noLabel = _products.Count(p => p.Source == LabelSource.NoEpaLabel);
        _status.Text = $"{_products.Count} products · {withNumber} with EPA labels · {needNumber} need a registration number · {noLabel} with no EPA label";
        if (Features.RemovesUnusedDocuments)
            _status.Text += "\n" + PaperCopy.Removal;
    }

    private void ApplyFilter()
    {
        var text = _search.Text?.Trim() ?? "";
        _list.ItemsSource = _products
            .Where(p => text.Length == 0
                        || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || (p.EpaRegNo?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToRow)
            .ToList();
    }

    private static ProductRow ToRow(CountyProduct p) => p switch
    {
        { Source: LabelSource.NoEpaLabel } => new(p, p.Name, "No EPA label — use the paper label and SDS", Palette.Muted),
        { HasRegNo: false } => new(p, p.Name, "EPA registration number needed — tap to add it", Colors.OrangeRed),
        { Confirmed: true } => new(p, p.Name, $"EPA Reg. No. {p.EpaRegNo}", Palette.Green),
        _ => new(p, p.Name, $"EPA Reg. No. {p.EpaRegNo} · not yet checked against the container", Palette.Muted),
    };

    private async Task DownloadAllAsync()
    {
        if (_downloading)
            return;
        _downloading = true;
        _download.IsEnabled = false;
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
                _status.Text = p.Current.Length > 0 ? $"Downloading {p.Done + 1} of {p.Total}: {p.Current}…" : "Finishing…");
            var summary = await AppServices.Library.DownloadAllLatestAsync(_products, progress);

            var message = $"{summary.Saved} label(s) downloaded, {summary.AlreadySaved} already on this device.";
            if (summary.Failed.Count > 0)
                message += $"\n\n{summary.Failed.Count} could not be downloaded:\n• " + string.Join("\n• ", summary.Failed);
            if (Features.RemovesUnusedDocuments)
                message += "\n\n" + PaperCopy.Removal;
            message += "\n\n" + PaperCopy.Short;
            await DisplayAlertAsync("Download finished", message, "OK");
        }
        finally
        {
            _downloading = false;
            _download.IsEnabled = true;
            ShowCounts();
        }
    }
}
