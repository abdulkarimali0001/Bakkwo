using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Bakkwo.App.Localization;

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
        Strings.LanguageChanged += SetUpAppMenu;
        base.OnFrameworkInitializationCompleted();
    }

    private void SetUpAppMenu()
    {
        if (NativeMenu.GetMenu(this) is not { } menu) return;
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            if (item.Gesture is null)
            {
                item.Header = Strings.About;
                item.Click -= OnAboutClick;
                item.Click += OnAboutClick;
            }
            else
            {
                item.Header = Strings.Quit;
                item.Click -= OnQuitClick;
                item.Click += OnQuitClick;
            }
        }
    }

    private void OnAboutClick(object? sender, EventArgs e) => _main?.ShowAbout();

    private void OnQuitClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
