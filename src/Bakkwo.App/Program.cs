using Avalonia;

namespace Bakkwo.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // A second copy (Explorer starts one per selected file) hands its files to the first and exits.
        if (!SingleInstance.TryBecomePrimary())
        {
            var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).Select(Path.GetFullPath).ToList();
            if (SingleInstance.Forward(files)) return 0;
        }
        SingleInstance.Listen();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
