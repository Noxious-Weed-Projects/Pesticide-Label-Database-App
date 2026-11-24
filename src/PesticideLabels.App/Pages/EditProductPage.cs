using PesticideLabels.Core;

namespace PesticideLabels.App.Pages;

public sealed record SearchRow(string RegNo, string Title, string Subtitle);

/// <summary>Set or correct the EPA registration number for a product, with a name search of PPLS.</summary>
public sealed class EditProductPage : ContentPage
{
    private readonly string _productId;
    private readonly Label _name = new() { FontSize = 24, FontFamily = "OpenSansSemibold" };
    private readonly Switch _hasLabel = new();
    private readonly Entry _regNo = new() { Placeholder = "For example 62719-519", Keyboard = Keyboard.Text, FontSize = 20 };
    private readonly CheckBox _confirmed = new();
    private readonly Entry _searchText = new() { Placeholder = "Part of the product name", ReturnType = ReturnType.Search };
    private readonly Button _searchButton = new() { Text = "Search EPA" };
    private readonly Label _searchStatus = new() { TextColor = Palette.Muted };
    private readonly VerticalStackLayout _results = new() { Spacing = 6 };
    private readonly VerticalStackLayout _regNoSection = new() { Spacing = 10 };
    private bool _loaded;

    public EditProductPage(string productId)
    {
        _productId = productId;
        Title = "EPA registration number";

        var save = new Button { Text = "Save", BackgroundColor = Palette.Green, TextColor = Colors.White, FontSize = 18 };
        save.Clicked += async (_, _) => await SaveAsync();
        _searchButton.Clicked += async (_, _) => await SearchAsync();
        _searchText.Completed += async (_, _) => await SearchAsync();
        _hasLabel.Toggled += (_, e) => _regNoSection.IsVisible = e.Value;
        // A number typed by hand is new information, so it has to be confirmed again.
        _regNo.TextChanged += (_, _) =>
        {
            if (_loaded)
                _confirmed.IsChecked = false;
        };

        _regNoSection.Add(Heading("EPA Reg. No. from the container"));
        _regNoSection.Add(_regNo);
        _regNoSection.Add(Row(_confirmed, new Label { Text = "I compared this number with the one printed on the container", VerticalOptions = LayoutOptions.Center }));
        _regNoSection.Add(Heading("Find the number by product name"));
        _regNoSection.Add(new Label { Text = "Search EPA, then tap the product that matches your container.", TextColor = Palette.Muted });
        _regNoSection.Add(_searchText);
        _regNoSection.Add(_searchButton);
        _regNoSection.Add(_searchStatus);
        _regNoSection.Add(_results);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 12,
                Children =
                {
                    _name,
                    Row(_hasLabel, new Label { Text = "This product has an EPA registration number", VerticalOptions = LayoutOptions.Center }),
                    _regNoSection,
                    save,
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
            return;
        var product = (await AppServices.Library.LoadProductsAsync()).FirstOrDefault(p => p.Id == _productId);
        if (product is null)
        {
            await Navigation.PopAsync();
            return;
        }
        _name.Text = product.Name;
        _hasLabel.IsToggled = product.Source == LabelSource.Ppls;
        _regNoSection.IsVisible = _hasLabel.IsToggled;
        _regNo.Text = product.EpaRegNo ?? "";
        _confirmed.IsChecked = product.Confirmed;
        _searchText.Text = product.SearchHint ?? product.Name;
        _loaded = true;
    }

    private async Task SearchAsync()
    {
        var text = _searchText.Text?.Trim() ?? "";
        if (text.Length < 3)
        {
            _searchStatus.Text = "Type at least three letters.";
            return;
        }

        _searchButton.IsEnabled = false;
        _searchStatus.Text = "Searching EPA…";
        _results.Children.Clear();
        try
        {
            var hits = await AppServices.Ppls.SearchByNameAsync(text);
            // State special-local-need numbers (for example KS120001) are not container registration numbers.
            var usable = hits.Where(h => PplsClient.TryNormalizeRegNo(h.EpaRegNo, out _)).Take(40).ToList();
            _searchStatus.Text = usable.Count == 0
                ? "EPA has no federally registered product with that name. Adjuvants and dyes are usually not registered."
                : $"{usable.Count} product(s) found. Tap the one on your container.";
            foreach (var hit in usable)
                _results.Add(ResultButton(hit));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _searchStatus.Text = $"{LabelLibrary.Describe(ex)} You can still type the number from the container.";
        }
        finally
        {
            _searchButton.IsEnabled = true;
        }
    }

    private Button ResultButton(PplsSearchHit hit)
    {
        var button = new Button
        {
            Text = $"{hit.EpaRegNo}   {hit.ProductName.Trim()}" + (hit.IsActive ? "" : "   (inactive)"),
            BackgroundColor = Colors.Transparent,
            TextColor = hit.IsActive ? Palette.Green : Palette.Muted,
            BorderColor = hit.IsActive ? Palette.Green : Palette.Muted,
            BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        button.Clicked += (_, _) => _regNo.Text = hit.EpaRegNo;
        return button;
    }

    private async Task SaveAsync()
    {
        var products = await AppServices.Library.LoadProductsAsync();
        var product = products.FirstOrDefault(p => p.Id == _productId);
        if (product is null)
            return;

        if (!_hasLabel.IsToggled)
        {
            product.Source = LabelSource.NoEpaLabel;
            product.EpaRegNo = null;
            product.Confirmed = false;
            product.Note ??= "No EPA label. Use the paper label and SDS from the supplier.";
        }
        else
        {
            var text = _regNo.Text?.Trim() ?? "";
            string? regNo = null;
            if (text.Length > 0 && !PplsClient.TryNormalizeRegNo(text, out regNo))
            {
                await DisplayAlertAsync("Check the number",
                    "An EPA Reg. No. is two or three groups of digits joined by dashes, for example 62719-519 or 66222-141-81927.", "OK");
                return;
            }
            product.Source = LabelSource.Ppls;
            product.EpaRegNo = regNo;
            product.Confirmed = regNo is not null && _confirmed.IsChecked;
        }

        await AppServices.Library.SaveProductsAsync(products);
        await Navigation.PopAsync();
    }

    private static Label Heading(string text) => new() { Text = text, FontSize = 18, FontFamily = "OpenSansSemibold", Margin = new Thickness(0, 8, 0, 0) };

    private static HorizontalStackLayout Row(View control, View label) => new() { Spacing = 10, Children = { control, label } };
}
