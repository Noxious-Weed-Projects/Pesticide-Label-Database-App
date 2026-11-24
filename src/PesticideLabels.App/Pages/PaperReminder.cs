namespace PesticideLabels.App.Pages;

/// <summary>The always-visible reminder that the app does not replace paper labels.</summary>
public sealed class PaperBanner : Border
{
    public PaperBanner()
    {
        BackgroundColor = Palette.WarningBackground;
        StrokeThickness = 0;
        Padding = new Thickness(14, 8);
        Content = new Label
        {
            Text = "⚠ " + PaperCopy.Short,
            TextColor = Palette.WarningText,
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        };
    }
}

/// <summary>Full-screen notice shown at launch. It must be acknowledged before the app can be used.</summary>
public sealed class PaperReminderPage : ContentPage
{
    public event EventHandler? Acknowledged;

    public PaperReminderPage()
    {
        BackgroundColor = Palette.WarningBackground;
        var button = new Button
        {
            Text = "I have paper copies with me",
            BackgroundColor = Palette.Green,
            TextColor = Colors.White,
            FontSize = 20,
            Padding = new Thickness(24, 16),
            HorizontalOptions = LayoutOptions.Center,
        };
        button.Clicked += async (_, _) =>
        {
            Acknowledged?.Invoke(this, EventArgs.Empty);
            await Navigation.PopModalAsync(animated: false);
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 32,
                Spacing = 22,
                MaximumWidthRequest = 720,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Text("Paper copies are still required", 30, bold: true),
                    Text("This tablet can lose power, signal or its saved labels. Before you leave the shop, " +
                         "make sure you have a paper copy of the label for every product you are carrying.", 20),
                    Text("The label on the container is the one you must follow. Labels shown here come from " +
                         "EPA records and may be a different version from the one on your container.", 20),
                    Text("If this app does not work, use the paper copy.", 20, bold: true),
                    button,
                },
            },
        };
    }

    // The Android back button must not skip the notice.
    protected override bool OnBackButtonPressed() => true;

    private static Label Text(string text, double size, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        TextColor = Palette.WarningText,
        FontFamily = bold ? "OpenSansSemibold" : "OpenSansRegular",
        HorizontalTextAlignment = TextAlignment.Center,
    };
}
