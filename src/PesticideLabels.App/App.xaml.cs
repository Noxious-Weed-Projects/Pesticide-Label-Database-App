using PesticideLabels.App.Pages;

namespace PesticideLabels.App;

public partial class App : Application
{
    private DateTime? _reminderAcknowledged;
    private bool _reminderShowing;

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var root = new NavigationPage(new ProductsPage())
        {
            BarBackgroundColor = Palette.Green,
            BarTextColor = Colors.White,
        };
        var window = new Window(root) { Title = "Rush County Pesticide Labels" };

        // Shown at every launch, and again the first time the app is brought back on a new day,
        // because tablets are often left running for days.
        root.Loaded += async (_, _) => await ShowPaperReminderAsync(root);
        window.Resumed += async (_, _) =>
        {
            if (_reminderAcknowledged is { } when && when.Date != DateTime.Now.Date)
                await ShowPaperReminderAsync(root);
        };
        return window;
    }

    private async Task ShowPaperReminderAsync(NavigationPage root)
    {
        if (_reminderShowing)
            return;
        _reminderShowing = true;
        var page = new PaperReminderPage();
        page.Acknowledged += (_, _) =>
        {
            _reminderAcknowledged = DateTime.Now;
            _reminderShowing = false;
        };
        await root.Navigation.PushModalAsync(page, animated: false);
    }
}
