using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bakkwo.UI;
using Bakkwo.UI.Localization;

namespace Bakkwo.Mobile;

public partial class App : Application
{
    /// <summary>Set by the Android (or iOS) project before the app starts.</summary>
    public static IPhonePlatform Platform { get; set; } = new NoPhonePlatform();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = Settings.Load();
        if (settings.LanguageChoice is { } language) Strings.Set(language);

        if (ApplicationLifetime is ISingleViewApplicationLifetime phone)
        {
            phone.MainView = new MainView(settings, Platform);
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Lets the phone screen run in a window on a computer, for trying it out.
            desktop.MainWindow = new Avalonia.Controls.Window
            {
                Title = Strings.AppName,
                Width = 400,
                Height = 820,
                Content = new MainView(settings, Platform),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
