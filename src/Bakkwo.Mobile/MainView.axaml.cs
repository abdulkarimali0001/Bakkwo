using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;
using Bakkwo.UI;
using Bakkwo.UI.Controls;
using Bakkwo.UI.Localization;
using Bakkwo.UI.Ring;

namespace Bakkwo.Mobile;

/// <summary>
/// The phone screen: pick files (or share them to 바꿔 from another app), choose a format on the ring,
/// then save or share the result. Conversions run on the phone; nothing is uploaded.
/// </summary>
public partial class MainView : UserControl
{
    // Same sizes as the desktop app. Longest side in pixels; applies to images.
    private static readonly int?[] MaxSizes = [null, 3840, 1920, 1280, 1080, 512];

    // Phones can't keep an unpacked folder anywhere useful, so "Extract to folder" isn't offered.
    private static readonly HashSet<string> NotOnPhones = ["folder"];

    private readonly IPhonePlatform _platform;
    private readonly ConversionEngine _engine;
    private readonly Settings _settings;
    private readonly ObservableCollection<ResultItem> _results = [];

    private FormatRing? _ring;
    private List<string> _pendingFiles = [];
    private bool _busy;
    private bool _touching;

    public MainView() : this(new Settings(), new NoPhonePlatform()) { }

    public MainView(Settings settings, IPhonePlatform platform)
    {
        _settings = settings;
        _platform = platform;
        _engine = new ConversionEngine(CreateRegistry(platform));
        InitializeComponent();

        QualitySlider.Value = Math.Clamp(settings.Quality, 10, 100);
        QualityLabel.Text = ((int)QualitySlider.Value).ToString();
        MaxSizeBox.SelectedIndex = Math.Clamp(settings.MaxSizeIndex, 0, MaxSizes.Length - 1);
        GrayscaleBox.IsChecked = settings.Grayscale;
        QualitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) QualityLabel.Text = ((int)QualitySlider.Value).ToString();
        };
        QualitySlider.ValueChanged += (_, _) => SaveOptions();
        MaxSizeBox.SelectionChanged += (_, _) => SaveOptions();
        GrayscaleBox.IsCheckedChanged += (_, _) => SaveOptions();

        ResultsList.ItemsSource = _results;
        _results.CollectionChanged += (_, _) => UpdateEmpty();

        PickButton.Click += async (_, _) => await PickAsync();
        ClearButton.Click += (_, _) => _results.Clear();
        LanguageButton.Click += (_, _) =>
        {
            Strings.Set(Strings.IsKorean ? AppLanguage.English : AppLanguage.Korean);
            _settings.Language = Strings.IsKorean ? "ko" : "en";
            _settings.Save();
        };
        AboutButton.Click += (_, _) => AboutLayer.IsVisible = true;
        AboutClose.Click += (_, _) => AboutLayer.IsVisible = false;

        Donut.PointerPressed += OnRingPressed;
        Donut.PointerMoved += OnRingMoved;
        Donut.PointerReleased += OnRingReleased;
        RingCancel.Click += (_, _) => HideRing();
        // Tapping the dimmed area around the ring closes it.
        RingLayer.PointerPressed += (_, e) =>
        {
            if (e.Source == RingLayer) HideRing();
        };

        Strings.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
        UpdateEmpty();
        CleanOldWork();

        Inbox.OnReceived(paths => Dispatcher.UIThread.Post(() => OpenFiles(paths)));

        // Android's back gesture closes the ring or About before it leaves the app.
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested += OnBackRequested;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBackRequested;
        };
    }

    private void OnBackRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (RingLayer.IsVisible) HideRing();
        else if (AboutLayer.IsVisible) AboutLayer.IsVisible = false;
        else return;
        e.Handled = true;
    }

    /// <summary>Everything that runs without FFmpeg or LibreOffice, plus the phone's own decoders.</summary>
    private static ConverterRegistry CreateRegistry(IPhonePlatform platform)
    {
        var registry = new ConverterRegistry()
            .Add(new ImageConverter())
            .Add(new ImageToPdfConverter())
            .Add(new DataConverter())
            .Add(new TextConverter())
            .Add(new WordConverter())
            .Add(new ArchiveConverter());
        foreach (var converter in platform.ExtraConverters) registry.Add(converter);
        return registry;
    }

    private void ApplyLanguage()
    {
        SubtitleText.Text = Strings.PhoneSubtitle;
        LanguageButton.Content = Strings.LanguageButton;
        PickButton.Content = Strings.PhonePickTitle;
        PickHint.Text = Strings.PhonePickHint;
        OptionsExpander.Header = Strings.PhoneOptions;
        QualityCaption.Text = Strings.Quality;
        MaxSizeCaption.Text = Strings.MaxSize;
        OriginalItem.Content = Strings.Original;
        // A ComboBox shows a snapshot of the selected item; reselect so "Original/원본" updates.
        var selected = MaxSizeBox.SelectedIndex;
        MaxSizeBox.SelectedIndex = -1;
        MaxSizeBox.SelectedIndex = selected;
        GrayscaleBox.Content = Strings.Grayscale;
        RecentCaption.Text = Strings.Recent;
        ClearButton.Content = Strings.ClearList;
        EmptyText.Text = Strings.PhoneEmpty;
        LimitsText.Text = Strings.PhoneLimits;
        RingHint.Text = Strings.PhoneTapFormat;
        RingCancel.Content = Strings.Cancel;
        Donut.CancelText = Strings.Cancel;
        if (!_busy) StatusText.Text = Strings.Ready;

        AboutTagline.Text = Strings.Tagline;
        AboutMeaning.Text = Strings.Meaning;
        AboutVersion.Text = Strings.Version(AppVersion);
        AboutMadeBy.Text = Strings.MadeBy;
        AboutInspired.Text = Strings.Inspired;
        AboutClose.Content = Strings.Close;

        foreach (var row in _results) row.RefreshLabels();
    }

    private static string AppVersion =>
        (typeof(MainView).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+')[0];

    private void UpdateEmpty()
    {
        EmptyText.IsVisible = _results.Count == 0;
        ClearButton.IsVisible = _results.Count > 0 && !_busy;
    }

    private void SaveOptions()
    {
        _settings.Quality = (int)QualitySlider.Value;
        _settings.MaxSizeIndex = MaxSizeBox.SelectedIndex;
        _settings.Grayscale = GrayscaleBox.IsChecked == true;
        _settings.Save();
    }

    // ------------------------------------------------------------ picking files

    private string InDirectory => Path.Combine(_platform.WorkDirectory, "in");
    private string OutDirectory => Path.Combine(_platform.WorkDirectory, "out");

    private async Task PickAsync()
    {
        if (_busy || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.PickerTitle,
            AllowMultiple = true,
        });
        if (picked.Count == 0) return;

        StatusText.Text = Strings.PhoneOpening;
        try
        {
            var paths = new List<string>();
            foreach (var file in picked)
            {
                // Android hands out content:// links, not paths: copy each file into the app's own folder.
                paths.Add(await CopyInAsync(file));
                file.Dispose();
            }
            OpenFiles(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusText.Text = Strings.SomethingWrong(ex.Message);
        }
    }

    private async Task<string> CopyInAsync(IStorageFile file)
    {
        if (file.TryGetLocalPath() is { } local && File.Exists(local)) return local;

        var folder = Path.Combine(InDirectory, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, SafeName(file.Name));
        await using var source = await file.OpenReadAsync();
        await using var copy = File.Create(target);
        await source.CopyToAsync(copy);
        return target;
    }

    private static string SafeName(string name)
    {
        var clean = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "file" : clean;
    }

    /// <summary>Removes copies and results from earlier days; today's stay so they can still be saved.</summary>
    private void CleanOldWork()
    {
        try
        {
            foreach (var dir in new[] { InDirectory, OutDirectory }.Where(Directory.Exists))
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (entry.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-1)) continue;
                    if (entry is DirectoryInfo d) d.Delete(recursive: true);
                    else entry.Delete();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only housekeeping.
        }
    }

    // ------------------------------------------------------------ the ring

    /// <summary>Files picked here or shared to 바꿔 from another app (already copied to local paths).</summary>
    public void OpenFiles(IReadOnlyList<string> paths)
    {
        if (_busy || paths.Count == 0) return;
        AboutLayer.IsVisible = false;
        _pendingFiles = paths.ToList();
        _ring = FormatRing.For(_pendingFiles, _engine, maxSuggestions: 7, slicesPerPage: 9, exclude: NotOnPhones);
        ShowPage();
        Donut.CenterText = paths.Count == 1 ? Path.GetFileName(paths[0]) : Strings.FilesCount(paths.Count);
        RingHint.Text = _ring.HasUnknownFiles ? Strings.UnknownTypes : Strings.PhoneTapFormat;
        RingLayer.IsVisible = true;
        StatusText.Text = Strings.Ready;
    }

    private void ShowPage()
    {
        Donut.Items = _ring?.CurrentItems() ?? [];
        Donut.Highlighted = DonutMenu.NoIndex;
    }

    private void HideRing()
    {
        RingLayer.IsVisible = false;
        _touching = false;
        Donut.Highlighted = DonutMenu.NoIndex;
    }

    private void OnRingPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        _touching = true;
        e.Pointer.Capture(Donut);
        Donut.Highlighted = Donut.HitTest(e.GetPosition(Donut));
    }

    private void OnRingMoved(object? sender, PointerEventArgs e)
    {
        // Finger down: the slice under it lights up and its hint shows in the middle.
        if (_touching) Donut.Highlighted = Donut.HitTest(e.GetPosition(Donut));
    }

    private async void OnRingReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_touching) return;
        _touching = false;
        e.Pointer.Capture(null);
        var index = Donut.HitTest(e.GetPosition(Donut));

        if (index == DonutMenu.CenterIndex)
        {
            HideRing();
            return;
        }
        if (index < 0 || index >= Donut.Items.Count)
        {
            Donut.Highlighted = DonutMenu.NoIndex;
            return;
        }
        var chosen = Donut.Items[index].Id;
        if (chosen == FormatRing.MoreId)
        {
            _ring?.NextPage();
            ShowPage();
            return;
        }

        var files = _pendingFiles;
        HideRing();
        await StartAsync(files, chosen);
    }

    // ------------------------------------------------------------ converting

    private ConversionOptions CurrentOptions() => new()
    {
        Quality = (int)QualitySlider.Value,
        MaxDimension = MaxSizes[Math.Clamp(MaxSizeBox.SelectedIndex, 0, MaxSizes.Length - 1)],
        Grayscale = GrayscaleBox.IsChecked == true,
        OutputDirectory = OutDirectory,
    };

    private async Task StartAsync(List<string> files, string targetId)
    {
        _busy = true;
        PickButton.IsEnabled = false;
        UpdateEmpty();
        Directory.CreateDirectory(OutDirectory);
        var options = CurrentOptions();
        try
        {
            if (targetId == FormatRing.ZipAllId) await ZipAllAsync(files, options);
            else await ConvertAsync(files, FormatCatalog.ById(targetId)!, options);
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.SomethingWrong(ex.Message);
        }
        finally
        {
            _busy = false;
            PickButton.IsEnabled = true;
            UpdateEmpty();
        }
    }

    private ResultItem NewRow(string title, string detail) => new(title, detail, SaveAsync, ShareAsync);

    private async Task ConvertAsync(List<string> files, FileFormat target, ConversionOptions options)
    {
        var rows = files.Select(f => NewRow($"{Path.GetFileName(f)} → {target.Label}", Strings.Converting)).ToList();
        for (var i = rows.Count - 1; i >= 0; i--) _results.Insert(0, rows[i]);
        StatusText.Text = Strings.ConvertingTo(files.Count, target.Label);

        var progress = new Progress<FileProgress>(p =>
        {
            var row = rows[p.Index];
            row.Progress = p.Fraction;
            if (p.Result is not { } result) return;

            row.IsRunning = false;
            if (result.Success)
            {
                row.OutputPath = result.OutputPath;
                var via = result.Route.Count > 1 ? result.RouteDescription : "";
                row.Detail = Strings.PhoneReady(Path.GetFileName(result.OutputPath)!, via, result.Elapsed.TotalSeconds);
            }
            else
            {
                row.Failed = true;
                row.Detail = Strings.Failed(result.Error ?? "");
            }
        });

        var results = await _engine.ConvertManyAsync(files, target, options, progress);
        var ok = results.Count(r => r.Success);
        StatusText.Text = ok == results.Count ? Strings.DoneConverted(ok, target.Label) : Strings.PartlyDone(ok, results.Count);
    }

    private async Task ZipAllAsync(List<string> files, ConversionOptions options)
    {
        var zip = FormatCatalog.ById("zip")!;
        var stem = files.Count == 1 ? FormatCatalog.StemOf(files[0]) : "Archive";
        var row = NewRow(files.Count == 1 ? $"{Path.GetFileName(files[0])} → ZIP" : Strings.ItemsToZip(files.Count), Strings.Zipping);
        _results.Insert(0, row);

        var output = OutputPaths.Reserve(files[0], zip, options, stem);
        try
        {
            await ArchiveConverter.ZipManyAsync(files, output);
            row.OutputPath = output;
            row.Detail = Path.GetFileName(output);
            StatusText.Text = Strings.DoneZipped;
        }
        catch (Exception ex)
        {
            File.Delete(output);
            row.Failed = true;
            row.Detail = Strings.Failed(ex.Message);
            StatusText.Text = Strings.ZipFailed;
        }
        finally
        {
            row.IsRunning = false;
        }
    }

    // ------------------------------------------------------------ save and share

    private async Task SaveAsync(ResultItem row)
    {
        if (row.OutputPath is not { } path || !File.Exists(path)) return;
        try
        {
            if (await _platform.SaveToDownloadsAsync(path) is { } place)
            {
                StatusText.Text = Strings.SavedTo(place);
                return;
            }
            // This phone can't save to Downloads directly: ask where.
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = Path.GetFileName(path) });
            if (file is null) return;
            await using (var target = await file.OpenWriteAsync())
            await using (var source = File.OpenRead(path))
            {
                await source.CopyToAsync(target);
            }
            StatusText.Text = Strings.SavedTo(file.Name);
            file.Dispose();
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.SaveFailed(ex.Message);
        }
    }

    private async Task ShareAsync(ResultItem row)
    {
        if (row.OutputPath is not { } path || !File.Exists(path)) return;
        try
        {
            await _platform.ShareAsync([path]);
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.SomethingWrong(ex.Message);
        }
    }
}
