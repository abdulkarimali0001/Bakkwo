using Avalonia;

namespace Bakkwo.Mobile.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}
