using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Bakkwo.UI;
using Bakkwo.UI.Localization;

namespace Bakkwo.App;

public partial class App : Application
{
    private MainWindow? _main;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = Settings.Load();
        if (settings.LanguageChoice is { } language) Strings.Set(language);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _main = new MainWindow(settings);
            desktop.MainWindow = _main;

            // Windows/Linux: "Open with Bakkwo" passes the files as arguments.
            var startupFiles = desktop.Args?.Where(a => File.Exists(a) || Directory.Exists(a)).ToList() ?? [];
            if (startupFiles.Count > 0) _main.Opened += (_, _) => _main.OpenFiles(startupFiles);

            // Files (or "come to the front") sent by another copy of Bakkwo.
            var main = _main;
            SingleInstance.OnReceived(paths => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (paths.Count > 0) main.OpenFiles(paths);
                else main.BringToFront();
            }));
        }

        // macOS: files dropped on the Dock icon or opened via Finder's "Open With" arrive as activation events.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is FileActivatedEventArgs files && _main is not null)
                {
                    var paths = files.Files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                    if (paths.Count > 0) _main.OpenFiles(paths);
                }
            };
        }

        SetUpAppMenu();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the macOS app menu once, in the startup language. Renaming items later makes Avalonia
    /// append the menu again instead of replacing it, so the menu follows the language on next launch.
    /// </summary>
    private void SetUpAppMenu()
    {
        var about = new NativeMenuItem(Strings.About);
        about.Click += OnAboutClick;
        var quit = new NativeMenuItem(Strings.Quit) { Gesture = KeyGesture.Parse("Cmd+Q") };
        quit.Click += OnQuitClick;

        var menu = new NativeMenu();
        menu.Items.Add(about);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        NativeMenu.SetMenu(this, menu);
    }

    private void OnAboutClick(object? sender, EventArgs e) => _main?.ShowAbout();

    private void OnQuitClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
