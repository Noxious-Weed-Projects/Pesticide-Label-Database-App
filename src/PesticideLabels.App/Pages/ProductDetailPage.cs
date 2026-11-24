using PesticideLabels.Core;

namespace PesticideLabels.App.Pages;

/// <summary>One county product: its EPA details and the list of label PDFs.</summary>
public sealed class ProductDetailPage : ContentPage
{
    private const int LabelsShownAtFirst = 4;

    private readonly string _productId;
    private readonly VerticalStackLayout _body = new() { Padding = 16, Spacing = 12 };
    private readonly ActivityIndicator _busy = new() { IsVisible = false, HorizontalOptions = LayoutOptions.Start };
    private CountyProduct? _product;
    private bool _showAllLabels;
    private bool _opening;
    private int _loadVersion;

    public ProductDetailPage(string productId)
    {
        _productId = productId;
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        grid.Add(new PaperBanner(), 0, 0);
        grid.Add(new ScrollView { Content = _body }, 0, 1);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var products = await AppServices.Library.LoadProductsAsync();
        _product = products.FirstOrDefault(p => p.Id == _productId);
        if (_product is null)
        {
            await Navigation.PopAsync();
            return;
        }
        Title = _product.Name;

        if (!_product.HasRegNo)
        {
            Render(null, null);
            return;
        }

        // Show what is saved on the device straight away, then check EPA for anything newer.
        var saved = await AppServices.Library.GetSavedProductAsync(_product.EpaRegNo!);
        if (version != _loadVersion)
            return;
        Render(saved.Product is null ? null : saved, "Checking EPA for the current label list…");

        var live = await AppServices.Library.GetProductAsync(_product.EpaRegNo!);
        if (version != _loadVersion)
            return;
        Render(live, null);
    }

    private void Render(ProductLookup? lookup, string? working)
    {
        var product = _product!;
        _body.Children.Clear();

        _body.Add(new Label { Text = product.Name, FontSize = 26, FontFamily = "OpenSansSemibold" });

        if (product.Source == LabelSource.NoEpaLabel)
        {
            _body.Add(Notice("No EPA label for this product", product.Note ?? "Use the paper label and SDS from the supplier.", warning: true));
            _body.Add(EditButton("This product does have an EPA Reg. No."));
            return;
        }

        if (!product.HasRegNo)
        {
            _body.Add(Notice("EPA registration number needed",
                "Several EPA products match this name. Read the EPA Reg. No. from the container and enter it so the right label is shown. " +
                "Until then, use your paper copy.", warning: true));
            _body.Add(EditButton("Enter the EPA Reg. No."));
            return;
        }

        _body.Add(new Label { Text = $"EPA Reg. No. {product.EpaRegNo}", FontSize = 18 });
        if (!product.Confirmed)
        {
            _body.Add(Notice("Not yet checked against the container",
                "This number was matched by product name only. Compare it with the EPA Reg. No. printed on the container before relying on this label.",
                warning: true));
        }
        _body.Add(EditButton(product.Confirmed ? "Change the EPA Reg. No." : "Check or change the EPA Reg. No."));

        if (working is not null)
            _body.Add(new Label { Text = working, TextColor = Palette.Muted });

        if (lookup?.Error is { } error)
        {
            var text = lookup.Product is null
                ? $"{error} {PaperCopy.OnFailure}"
                : $"{error} Showing the copy saved on this device on {lookup.FetchedAt:MMMM d, yyyy}; a newer label may exist. {PaperCopy.OnFailure}";
            _body.Add(Notice("Could not get this product from EPA", text, warning: false));
        }

        if (lookup?.Product is not { } ppls)
            return;

        if (ppls.IsRestrictedUse)
            _body.Add(Notice("Restricted Use Pesticide", "EPA lists this product as restricted use.", warning: false));

        var details = new VerticalStackLayout { Spacing = 4 };
        details.Add(Fact("EPA product name", ppls.ProductName));
        details.Add(Fact("Registrant", ppls.Companies.FirstOrDefault()?.Name));
        details.Add(Fact("Status", ppls.ProductStatus));
        details.Add(Fact("Signal word", ppls.SignalWord));
        details.Add(Fact("Formulation", string.Join(", ", ppls.Formulations.Select(f => f.Formulation).Where(f => !string.IsNullOrWhiteSpace(f)))));
        foreach (var ingredient in ppls.ActiveIngredients)
            details.Add(Fact("Active ingredient", ingredient.Percent is { } pct ? $"{ingredient.Name} ({pct:0.##}%)" : ingredient.Name));
        if (!string.Equals(ppls.EpaRegNo, product.EpaRegNo, StringComparison.Ordinal))
            details.Add(Fact("Label filed under", $"{ppls.EpaRegNo} (parent registration of this distributor product)"));
        _body.Add(details);

        _body.Add(new Label { Text = "Labels accepted by EPA", FontSize = 20, FontFamily = "OpenSansSemibold", Margin = new Thickness(0, 10, 0, 0) });
        _body.Add(new Label
        {
            Text = "These are the labels EPA accepted for this registration. The label on your container may be a different version, and it is the one to follow.",
            TextColor = Palette.Muted,
            FontSize = 14,
        });
        _body.Add(_busy);

        var labels = ppls.LabelsNewestFirst;
        if (labels.Count == 0)
        {
            _body.Add(Notice("No label PDF on file", $"EPA lists no label PDF for this registration. {PaperCopy.OnFailure}", warning: true));
            return;
        }

        var shown = _showAllLabels ? labels : [.. labels.Take(LabelsShownAtFirst)];
        for (var i = 0; i < shown.Count; i++)
            _body.Add(LabelButton(shown[i], newest: i == 0));

        if (!_showAllLabels && labels.Count > LabelsShownAtFirst)
        {
            var more = new Button { Text = $"Show {labels.Count - LabelsShownAtFirst} older labels", BackgroundColor = Colors.Transparent, TextColor = Palette.Green };
            more.Clicked += (_, _) =>
            {
                _showAllLabels = true;
                Render(lookup, working);
            };
            _body.Add(more);
        }
    }

    private Button LabelButton(PplsLabelFile file, bool newest)
    {
        var date = file.AcceptedDate is { } d ? d.ToString("MMMM d, yyyy") : file.FileName;
        var saved = AppServices.Library.IsLabelSaved(file.FileName);
        var button = new Button
        {
            Text = (newest ? "Newest label — accepted " : "Accepted ") + date + (saved ? "   ✓ saved on this device" : "   (needs internet)"),
            BackgroundColor = newest ? Palette.Green : Colors.Transparent,
            TextColor = newest ? Colors.White : Palette.Green,
            BorderColor = Palette.Green,
            BorderWidth = 1,
            FontSize = 16,
            Padding = new Thickness(14, 12),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        button.Clicked += async (_, _) => await OpenLabelAsync(file, date);
        return button;
    }

    private async Task OpenLabelAsync(PplsLabelFile file, string date)
    {
        if (_opening)
            return;
        _opening = true;
        _busy.IsVisible = _busy.IsRunning = true;
        try
        {
            var path = await AppServices.Library.EnsureLabelAsync(file.FileName);
            await Navigation.PushAsync(new LabelViewerPage(path, $"{_product!.Name} — {date}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            await DisplayAlertAsync("Could not open this label", $"{LabelLibrary.Describe(ex)}\n\n{PaperCopy.OnFailure}", "OK");
        }
        finally
        {
            _busy.IsVisible = _busy.IsRunning = false;
            _opening = false;
        }
    }

    private Button EditButton(string text)
    {
        var button = new Button { Text = text, HorizontalOptions = LayoutOptions.Start, BackgroundColor = Colors.Transparent, TextColor = Palette.Green, BorderColor = Palette.Green, BorderWidth = 1 };
        button.Clicked += async (_, _) => await Navigation.PushAsync(new EditProductPage(_productId));
        return button;
    }

    private static View Fact(string name, string? value) => new Label
    {
        FontSize = 16,
        IsVisible = !string.IsNullOrWhiteSpace(value),
        FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = name + ": ", FontFamily = "OpenSansSemibold" },
                new Span { Text = value ?? "" },
            },
        },
    };

    private static Border Notice(string title, string text, bool warning) => new()
    {
        BackgroundColor = warning ? Palette.WarningBackground : Palette.ErrorBackground,
        StrokeThickness = 0,
        Padding = 12,
        Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = title, FontFamily = "OpenSansSemibold", FontSize = 16, TextColor = warning ? Palette.WarningText : Palette.ErrorText },
                new Label { Text = text, FontSize = 15, TextColor = warning ? Palette.WarningText : Palette.ErrorText },
            },
        },
    };
}
