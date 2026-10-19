using System.Reflection;
using Avalonia.Controls;
using Bakkwo.App.Localization;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Formats;

namespace Bakkwo.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();

        var registry = ConverterRegistry.CreateDefault();
        var mediaReady = registry.Converters.All(c => c.IsAvailable);

        Title = Strings.About;
        NameText.Text = Strings.AppName;
        TaglineText.Text = Strings.Tagline;
        MeaningText.Text = Strings.Meaning;
        VersionText.Text = Strings.Version(AppVersion);
        FormatsText.Text = Strings.FormatsSupported(FormatCatalog.All.Count(f => !f.IsFolder));
        EngineText.Text = mediaReady ? Strings.AllReady : Strings.FfmpegMissing;
        MadeByText.Text = Strings.MadeBy;
        InspiredText.Text = Strings.Inspired;
        CloseButton.Content = Strings.Close;
    }

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";
}
