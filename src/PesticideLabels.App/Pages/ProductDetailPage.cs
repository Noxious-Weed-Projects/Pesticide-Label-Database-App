using PesticideLabels.Core;

namespace PesticideLabels.App.Pages;

/// <summary>One county product: its EPA details and labels, and its label and Safety Data Sheet from CDMS.</summary>
public sealed class ProductDetailPage : ContentPage
{
    private const int LabelsShownAtFirst = 4;

    private readonly string _productId;
    private readonly VerticalStackLayout _body = new() { Padding = 16, Spacing = 12 };
    private readonly ActivityIndicator _busy = new() { IsVisible = false, HorizontalOptions = LayoutOptions.Start };
    private readonly VerticalStackLayout _sds = new() { Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };
    private readonly ActivityIndicator _sdsBusy = new() { IsVisible = false, HorizontalOptions = LayoutOptions.Start };
    private CountyProduct? _product;
    private bool _showAllLabels;
    private bool _showSupplemental;
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

        // CDMS is a different site, so it is looked up alongside EPA.
        var sds = LoadSdsAsync(version);

        if (!_product.HasRegNo)
        {
            Render(null, null);
            await sds;
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
        await sds;
    }

    private void Render(ProductLookup? lookup, string? working)
    {
        RenderLabels(lookup, working);
        _body.Add(_sds);
    }

    private void RenderLabels(ProductLookup? lookup, string? working)
    {
        var product = _product!;
        _body.Children.Clear();

        _body.Add(new Label { Text = product.Name, FontSize = 26, FontFamily = "OpenSansSemibold" });

        if (product.Source == LabelSource.NoEpaLabel)
        {
            _body.Add(Notice("No EPA label for this product",
                (product.Note ?? "Use the paper label and SDS from the supplier.") + " The manufacturer's label may be on CDMS, below.", warning: true));
            _body.Add(EditButton("This product does have an EPA Reg. No."));
            return;
        }

        if (!product.HasRegNo)
        {
            _body.Add(Notice("EPA registration number needed",
                "Several EPA products match this name. Read the EPA Reg. No. from the container and enter it so the right label is shown. " +
                "Until then, use your paper copy. The manufacturer's label may be on CDMS, below.", warning: true));
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
            _body.Add(Notice("No label PDF on file",
                $"EPA lists no label PDF for this registration. The manufacturer's label may be on CDMS, below. {PaperCopy.OnFailure}", warning: true));
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

    // ---- Labels and Safety Data Sheets from CDMS -----------------------------------------

    private const string PaperSds = "Use the paper label and SDS from the supplier.";
    private const int CandidatesShown = 12;

    private async Task LoadSdsAsync(int version)
    {
        var product = _product!;
        try
        {
            RenderSds(await AppServices.Library.GetSavedSdsAsync(product), "Checking CDMS for the label and Safety Data Sheet…");
            var live = await AppServices.Library.FindSdsAsync(product);
            if (version != _loadVersion)
                return;
            // A match made by EPA Reg. No. is remembered so the document list is there without a connection.
            if (live is { Match: { } match, Error: null } && product.CdmsProductId != match.Id)
                await SaveSdsMatchAsync(match.Id);
            RenderSds(live, null);
        }
        catch (IOException ex)
        {
            RenderSds(new SdsLookup(null, [], [], false, null, ex.Message), null);
        }
    }

    private async Task SaveSdsMatchAsync(int? cdmsProductId)
    {
        var products = await AppServices.Library.LoadProductsAsync();
        if (products.FirstOrDefault(p => p.Id == _productId) is not { } saved)
            return;
        saved.CdmsProductId = cdmsProductId;
        await AppServices.Library.SaveProductsAsync(products);
        _product!.CdmsProductId = cdmsProductId;
    }

    private void RenderSds(SdsLookup? lookup, string? working)
    {
        _sds.Children.Clear();
        _sds.Add(new Label { Text = "Label and Safety Data Sheet from CDMS", FontSize = 20, FontFamily = "OpenSansSemibold" });
        _sds.Add(new Label
        {
            Text = "From the CDMS Label Database (cdms.net), where manufacturers post their own labels and sheets. " +
                   "Check that the product name and manufacturer match your container. The label on the container is the one to follow.",
            TextColor = Palette.Muted,
            FontSize = 14,
        });
        if (working is not null)
            _sds.Add(new Label { Text = working, TextColor = Palette.Muted });
        if (lookup is null)
            return;

        if (lookup.Error is { } error)
        {
            var text = lookup.Match is null
                ? $"{error} {PaperSds}"
                : $"{error} Showing the list saved on this device on {lookup.FetchedAt:MMMM d, yyyy}; newer documents may exist.";
            _sds.Add(Notice("Could not get this product from CDMS", text, warning: false));
        }

        if (lookup.Match is { } match)
        {
            var details = new VerticalStackLayout { Spacing = 4 };
            details.Add(Fact("CDMS product", match.Name.Trim()));
            details.Add(Fact("Manufacturer", match.Manufacturer));
            details.Add(Fact("EPA Reg. No. on CDMS", match.EpaRegNo));
            _sds.Add(details);
            _sds.Add(_sdsBusy);

            // The product's own label first; 24(c), 2(ee) and other supplemental documents on request.
            var main = lookup.Labels.Where(l => l.IsMainLabel).ToList();
            var supplemental = lookup.Labels.Where(l => !l.IsMainLabel).ToList();
            if (main.Count == 0)
                (main, supplemental) = (supplemental, main);

            _sds.Add(SubHeading("Manufacturer's label"));
            if (main.Count == 0 && lookup.Error is null)
                _sds.Add(Notice("No label on CDMS", $"CDMS lists this product but has no label for it. {PaperCopy.OnFailure}", warning: true));
            for (var i = 0; i < main.Count; i++)
                _sds.Add(CdmsButton(main[i], "Label", filled: i == 0 && !_product!.HasRegNo));
            if (supplemental.Count > 0 && !_showSupplemental)
            {
                var more = new Button
                {
                    Text = $"Show {supplemental.Count} supplemental label document(s)",
                    HorizontalOptions = LayoutOptions.Start,
                    BackgroundColor = Colors.Transparent,
                    TextColor = Palette.Green,
                };
                more.Clicked += (_, _) =>
                {
                    _showSupplemental = true;
                    RenderSds(lookup, working);
                };
                _sds.Add(more);
            }
            else
            {
                foreach (var document in supplemental)
                    _sds.Add(CdmsButton(document, string.IsNullOrWhiteSpace(document.DocType) ? "Label document" : document.DocType.Trim(), filled: false));
            }

            _sds.Add(SubHeading("Safety Data Sheet"));
            if (lookup.Sheets.Count == 0 && lookup.Error is null)
                _sds.Add(Notice("No Safety Data Sheet on CDMS", $"CDMS lists this product but has no Safety Data Sheet for it. {PaperSds}", warning: true));
            foreach (var sheet in lookup.Sheets)
                _sds.Add(CdmsButton(sheet, "Safety Data Sheet", filled: false));

            var other = new Button { Text = "Not the product on your container? Choose another", HorizontalOptions = LayoutOptions.Start, BackgroundColor = Colors.Transparent, TextColor = Palette.Green };
            other.Clicked += async (_, _) => await ChooseAnotherSdsProductAsync();
            _sds.Add(other);
            return;
        }

        if (lookup.Error is not null)
            return;

        if (lookup.Candidates.Count == 0)
        {
            _sds.Add(Notice("Not found on CDMS",
                $"CDMS has no product with this name. Not every manufacturer posts its products there. {PaperSds}", warning: true));
            return;
        }

        _sds.Add(new Label { Text = "CDMS lists these products with a similar name. Choose the one that matches your container.", FontSize = 16 });
        _sds.Add(_sdsBusy);
        foreach (var candidate in lookup.Candidates.Take(CandidatesShown))
            _sds.Add(CandidateButton(candidate));
        if (lookup.Candidates.Count > CandidatesShown)
            _sds.Add(new Label { Text = $"{lookup.Candidates.Count - CandidatesShown} more are not shown.", TextColor = Palette.Muted });
    }

    private Button CandidateButton(CdmsProduct candidate)
    {
        var regNo = string.IsNullOrWhiteSpace(candidate.EpaRegNo) ? "" : $"   EPA {candidate.EpaRegNo}";
        var button = new Button
        {
            Text = $"{candidate.Name.Trim()} — {candidate.Manufacturer}{regNo}" + (candidate.IsUs ? "" : "   (not a US product)"),
            BackgroundColor = Colors.Transparent,
            TextColor = candidate.IsUs ? Palette.Green : Palette.Muted,
            BorderColor = candidate.IsUs ? Palette.Green : Palette.Muted,
            BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        button.Clicked += async (_, _) =>
        {
            try
            {
                await AppServices.Library.RememberSdsMatchAsync(candidate);
                await SaveSdsMatchAsync(candidate.Id);
            }
            catch (IOException ex)
            {
                await DisplayAlertAsync("Could not save the choice", ex.Message, "OK");
                return;
            }
            await LoadSdsAsync(_loadVersion);
        };
        return button;
    }

    private async Task ChooseAnotherSdsProductAsync()
    {
        if (_opening)
            return;
        _opening = true;
        _sdsBusy.IsVisible = _sdsBusy.IsRunning = true;
        try
        {
            // Searching by name alone lists every product with a similar name instead of settling on one.
            var byName = new CountyProduct { Name = _product!.Name, SearchHint = _product.SearchHint };
            var lookup = await AppServices.Library.FindSdsAsync(byName);
            if (lookup.Error is { } error)
                await DisplayAlertAsync("Could not search CDMS", error, "OK");
            else
                RenderSds(lookup, null);
        }
        finally
        {
            _sdsBusy.IsVisible = _sdsBusy.IsRunning = false;
            _opening = false;
        }
    }

    private static Label SubHeading(string text) =>
        new() { Text = text, FontSize = 17, FontFamily = "OpenSansSemibold", Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>A button that opens one CDMS document; <paramref name="kind"/> says what it is.</summary>
    private Button CdmsButton(CdmsDocument sheet, string kind, bool filled)
    {
        var languages = sheet.LanguageCodes.Count > 0 ? $" ({string.Join(", ", sheet.LanguageCodes)})" : "";
        var description = string.IsNullOrWhiteSpace(sheet.Description) ? sheet.FileName : sheet.Description.Trim();
        var saved = AppServices.Library.IsSdsSaved(sheet.FileName);
        var button = new Button
        {
            Text = $"{kind}{languages} — {description}" + (saved ? "   ✓ saved on this device" : "   (needs internet)"),
            BackgroundColor = filled ? Palette.Green : Colors.Transparent,
            TextColor = filled ? Colors.White : Palette.Green,
            BorderColor = Palette.Green,
            BorderWidth = 1,
            FontSize = 16,
            Padding = new Thickness(14, 12),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        button.Clicked += async (_, _) => await OpenCdmsDocumentAsync(sheet, kind);
        return button;
    }

    private async Task OpenCdmsDocumentAsync(CdmsDocument sheet, string kind)
    {
        if (_opening)
            return;
        _opening = true;
        _sdsBusy.IsVisible = _sdsBusy.IsRunning = true;
        try
        {
            var path = await AppServices.Library.EnsureSdsAsync(sheet.FileName);
            await Navigation.PushAsync(new LabelViewerPage(path, $"{_product!.Name} — {kind} (CDMS)"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            await DisplayAlertAsync("Could not open this document", $"{LabelLibrary.Describe(ex, LabelLibrary.Cdms)}\n\n{PaperSds}", "OK");
        }
        finally
        {
            _sdsBusy.IsVisible = _sdsBusy.IsRunning = false;
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
