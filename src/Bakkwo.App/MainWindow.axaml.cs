using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bakkwo.App.Controls;
using Bakkwo.App.Localization;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;

namespace Bakkwo.App;

public partial class MainWindow : Window
{
    private const string ZipAllId = "__zip_all__";
    private const string MoreId = "__more__";
    private const int SlicesPerPage = 11;

    /// <summary>Overall popularity, used to order the formats behind "More".</summary>
    private static readonly string[] Popular =
        ["mp4", "mp3", "mov", "gif", "jpg", "png", "pdf", "docx", "webp", "m4a", "wav", "webm", "mkv", "md", "txt",
         "html", "csv", "json", "xlsx", "srt", "zip", "folder"];

    private int _page;
    private readonly Avalonia.Threading.DispatcherTimer _moreTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    // Longest side in pixels; applies to images and videos (1920 ≈ 1080p, 1280 ≈ 720p for 16:9).
    private static readonly int?[] MaxSizes = [null, 3840, 1920, 1280, 1080, 512];

    private readonly ConversionEngine _engine = new(ConverterRegistry.CreateDefault());
    private readonly ObservableCollection<RecentItem> _recent = [];
    private List<string> _pendingFiles = [];
    private readonly Settings _settings;
    private bool _pinned;
    private bool _busy;

    public MainWindow() : this(new Settings()) { }

    public MainWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        QualitySlider.Value = Math.Clamp(settings.Quality, 10, 100);
        QualityLabel.Text = ((int)QualitySlider.Value).ToString();
        MaxSizeBox.SelectedIndex = Math.Clamp(settings.MaxSizeIndex, 0, MaxSizes.Length - 1);
        GrayscaleBox.IsChecked = settings.Grayscale;

        RecentList.ItemsSource = _recent;
        QualitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) QualityLabel.Text = ((int)QualitySlider.Value).ToString();
        };

        Root.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        Root.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Root.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        Root.AddHandler(DragDrop.DropEvent, OnDrop);

        Overlay.PointerMoved += OnOverlayPointerMoved;
        // While dragging, resting on "More" turns the page.
        _moreTimer.Tick += (_, _) =>
        {
            _moreTimer.Stop();
            if (Donut.IsVisible && Donut.Highlighted >= 0 && Donut.Items[Donut.Highlighted].Id == MoreId) NextPage();
        };
        Overlay.PointerPressed += OnOverlayPointerPressed;
        OpenButton.Click += OnOpenClick;
        AboutButton.Click += (_, _) => ShowAbout();
        LanguageButton.Click += (_, _) =>
        {
            Strings.Set(Strings.IsKorean ? AppLanguage.English : AppLanguage.Korean);
            _settings.Language = Strings.IsKorean ? "ko" : "en";
            _settings.Save();
        };
        Strings.LanguageChanged += ApplyLanguage;
        Closing += (_, _) => SaveOptions();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HideDonut();
        };

        ApplyLanguage();
    }

    /// <summary>Sets every visible string from <see cref="Strings"/>; runs again when the language changes.</summary>
    private void ApplyLanguage()
    {
        Title = Strings.AppName;
        SubtitleText.Text = Strings.Subtitle;
        LanguageButton.Content = Strings.LanguageButton;
        ToolTip.SetTip(AboutButton, Strings.About);
        QualityCaption.Text = Strings.Quality;
        MaxSizeCaption.Text = Strings.MaxSize;
        OriginalItem.Content = Strings.Original;
        // A ComboBox shows a snapshot of the selected item; reselect so "Original/원본" updates.
        var selected = MaxSizeBox.SelectedIndex;
        MaxSizeBox.SelectedIndex = -1;
        MaxSizeBox.SelectedIndex = selected;
        GrayscaleBox.Content = Strings.Grayscale;
        DropTitle.Text = Strings.DropTitle;
        DropHint.Text = Strings.DropHint;
        OpenButton.Content = Strings.ChooseFiles;
        RecentCaption.Text = Strings.Recent;
        if (!_busy && !Donut.IsVisible) StatusText.Text = Strings.Ready;

        EngineText.Text = Strings.EngineStatus(_engine.Registry.Converters.Where(c => !c.IsAvailable).Select(c => c.Name));

        Donut.CancelText = Strings.Cancel;
        foreach (var row in _recent) row.RefreshLabels();
    }

    public void ShowAbout() => new AboutWindow().ShowDialog(this);

    /// <summary>Files opened from Finder ("Open With", the Dock icon) or passed on the command line.</summary>
    public void OpenFiles(IReadOnlyList<string> paths)
    {
        if (_busy || paths.Count == 0) return;
        Activate();
        ShowDonut(paths.ToList(), new Point(Overlay.Bounds.Width / 2, Overlay.Bounds.Height / 2 + 40), pinned: true);
        StatusText.Text = Strings.OpenedFromFinder(paths.Count);
    }

    private void SaveOptions()
    {
        _settings.Quality = (int)QualitySlider.Value;
        _settings.MaxSizeIndex = MaxSizeBox.SelectedIndex;
        _settings.Grayscale = GrayscaleBox.IsChecked == true;
        _settings.Save();
    }

    // ------------------------------------------------------------ drag and drop

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_busy)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        DropOutline.Opacity = 1;

        // Most platforms expose the file list while dragging; if not, the ring opens on drop instead.
        var files = GetPaths(e);
        if (files.Count > 0) ShowDonut(files, e.GetPosition(Overlay), pinned: false);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _busy ? DragDropEffects.None : DragDropEffects.Copy;
        if (Donut.IsVisible && !_pinned)
        {
            MoveDonutIfFar(e.GetPosition(Overlay));
            var hit = Donut.HitTest(e.GetPosition(Donut));
            var onMore = hit >= 0 && Donut.Items[hit].Id == MoreId;
            if (hit != Donut.Highlighted)
            {
                Donut.Highlighted = hit;
                _moreTimer.Stop();
                if (onMore) _moreTimer.Start();
            }
        }
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        if (!_pinned) HideDonut();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = GetPaths(e);
        if (files.Count == 0 || _busy) return;

        if (Donut.IsVisible && !_pinned && Donut.Highlighted >= 0)
        {
            var chosen = Donut.Items[Donut.Highlighted].Id;
            if (chosen == MoreId)
            {
                NextPage();
                Pin();
                return;
            }
            HideDonut();
            await StartAsync(files, chosen);
        }
        else
        {
            // Dropped in the middle or outside the ring: keep it open so a format can be clicked.
            ShowDonut(files, e.GetPosition(Overlay), pinned: true);
        }
    }

    private static List<string> GetPaths(DragEventArgs e)
    {
        try
        {
            return e.Data.GetFiles()?
                .Select(item => item.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!)
                .ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    // ------------------------------------------------------------ the ring

    private void ShowDonut(List<string> files, Point at, bool pinned)
    {
        var formats = files.Select(FormatCatalog.Detect).ToList();
        var featured = new List<DonutItem>();
        var rest = new List<DonutItem>();

        if (formats.All(f => f is not null))
        {
            var sources = formats.Select(f => f!).ToList();
            var targets = _engine.Planner.CommonTargets(sources).ToList();

            // Only offer subtitle formats when every video has a text subtitle track (picture ones can't become SRT).
            if (targets.Any(t => t.Category == FormatCategory.Subtitle)
                && sources.Any(f => f.Category == FormatCategory.Video)
                && _engine.Registry.Converters.OfType<FfmpegConverter>().FirstOrDefault() is { } ffmpeg
                && !files.Take(5).All(ffmpeg.HasTextSubtitles))
            {
                targets.RemoveAll(t => t.Category == FormatCategory.Subtitle);
            }

            var single = sources.Select(f => f.Id).Distinct().Count() == 1 ? sources[0].Id : null;
            var picks = Suggested(sources);

            // First ring: up to 7 suggestions for this kind of file, in a fixed, predictable order.
            foreach (var id in picks)
            {
                if (featured.Count >= MaxSuggestions) break;
                if (id == single) continue;
                if (targets.FirstOrDefault(t => t.Id == id) is { } target) featured.Add(Item(target, single));
            }
            // "Make it smaller" (same format) is a top choice for photos and videos.
            if (single is not null && targets.FirstOrDefault(t => t.Id == single) is { } self)
            {
                featured.Insert(Math.Min(2, featured.Count), Item(self, single));
                if (featured.Count > MaxSuggestions) featured.RemoveAt(featured.Count - 1);
            }

            // Everything else, behind "More", popular formats first.
            rest.AddRange(targets
                .Where(t => featured.All(f => f.Id != t.Id))
                .Select((t, i) => (t, i))
                .OrderBy(x => Array.IndexOf(Popular, x.t.Id) is var p and >= 0 ? p : 100 + x.i)
                .Select(x => Item(x.t, single)));
        }
        if (featured.Concat(rest).All(i => i.Id != "zip"))
        {
            rest.Add(new DonutItem(ZipAllId, "ZIP", Strings.CaptionArchive, files.Count > 1 ? Strings.ZipAllHint : Strings.ZipHint));
        }
        if (featured.Count == 0)
        {
            // Nothing known to suggest (unknown files): show what there is.
            featured.AddRange(rest.Take(MaxSuggestions));
            rest.RemoveRange(0, featured.Count);
        }

        _pendingFiles = files;
        _featured = featured;
        _rest = rest;
        _page = 0;
        ShowPage();

        Donut.CenterText = files.Count == 1 ? Path.GetFileName(files[0].TrimEnd('/', '\\')) : Strings.FilesCount(files.Count);
        PlaceDonut(at);
        Donut.IsVisible = true;

        _pinned = pinned;
        Overlay.IsHitTestVisible = pinned;
        _moreTimer.Stop();
        StatusText.Text = formats.Any(f => f is null)
            ? Strings.UnknownTypes
            : pinned ? Strings.ClickFormat : Strings.ReleaseOverFormat;
    }

    private const int MaxSuggestions = 7;

    /// <summary>What most people want for each kind of file, best first.</summary>
    private static IEnumerable<string> Suggested(IReadOnlyList<FileFormat> sources) =>
        sources.Select(f => f.Category).Distinct().ToList() switch
        {
            // "srt" only exists when the video has text subtitles; then it's one of the most useful picks.
            [FormatCategory.Video] => ["mp4", "mov", "mp3", "srt", "gif", "m4a", "webm", "mkv"],
            [FormatCategory.Audio] => ["mp3", "m4a", "wav", "flac", "opus", "aac", "ogg"],
            [FormatCategory.Image] => ["jpg", "png", "webp", "pdf", "ico", "gif"],
            [FormatCategory.Document] => ["pdf", "docx", "md", "txt", "html", "odt"],
            [FormatCategory.Text] => ["html", "pdf", "docx", "txt", "md"],
            [FormatCategory.Data] => ["json", "csv", "yaml", "xml", "tsv"],
            [FormatCategory.Subtitle] => ["srt", "vtt", "ass"],
            [FormatCategory.Archive] => ["folder", "zip", "tgz", "tar"],
            _ => Popular,
        };

    /// <summary>A ring slice: format name, a small caption ("audio", "smaller"), and the hint shown in the middle.</summary>
    private static DonutItem Item(FileFormat target, string? sameAs)
    {
        if (target.IsFolder) return new DonutItem(target.Id, Strings.Extract, Strings.CaptionFolder, Strings.ExtractHint);
        if (target.Id == sameAs) return new DonutItem(target.Id, target.Label, Strings.Smaller, Strings.SmallerHint(target.Label));
        return new DonutItem(target.Id, target.Label, Strings.Caption(target.Category), Strings.ConvertHint(target));
    }

    private List<DonutItem> _featured = [];
    private List<DonutItem> _rest = [];

    private int PageCount => _rest.Count == 0 ? 1 : 1 + (int)Math.Ceiling(_rest.Count / (double)SlicesPerPage);

    /// <summary>Page 0: the suggestions. Further pages: everything else, 11 at a time. "More" cycles through.</summary>
    private void ShowPage()
    {
        var items = _page == 0
            ? new List<DonutItem>(_featured)
            : _rest.Skip((_page - 1) * SlicesPerPage).Take(SlicesPerPage).ToList();

        if (PageCount > 1)
        {
            var last = _page == PageCount - 1;
            items.Add(new DonutItem(MoreId,
                last ? Strings.BackLabel : Strings.MoreLabel,
                $"{_page + 1}/{PageCount}",
                last ? Strings.BackHint : Strings.MoreHint(_rest.Count),
                Special: true));
        }
        Donut.Items = items;
        Donut.Highlighted = DonutMenu.NoIndex;
    }

    private void NextPage()
    {
        _page = (_page + 1) % PageCount;
        ShowPage();
    }

    private void Pin()
    {
        _pinned = true;
        Overlay.IsHitTestVisible = true;
        StatusText.Text = Strings.ClickFormat;
    }

    private void PlaceDonut(Point center)
    {
        var size = Donut.Diameter;
        var maxX = Math.Max(0, Overlay.Bounds.Width - size);
        var maxY = Math.Max(0, Overlay.Bounds.Height - size);
        Canvas.SetLeft(Donut, Math.Clamp(center.X - size / 2, 0, maxX));
        Canvas.SetTop(Donut, Math.Clamp(center.Y - size / 2, 0, maxY));
    }

    /// <summary>While dragging, the ring follows the cursor only when it wanders well away from it.</summary>
    private void MoveDonutIfFar(Point cursor)
    {
        var center = new Point(Canvas.GetLeft(Donut) + Donut.Diameter / 2, Canvas.GetTop(Donut) + Donut.Diameter / 2);
        var dx = cursor.X - center.X;
        var dy = cursor.Y - center.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > DonutMenu.OuterRadius + 60) PlaceDonut(cursor);
    }

    private void HideDonut()
    {
        _moreTimer.Stop();
        Donut.IsVisible = false;
        _pinned = false;
        Overlay.IsHitTestVisible = false;
        DropOutline.Opacity = 0.8;
        if (!_busy) StatusText.Text = Strings.Ready;
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pinned) Donut.Highlighted = Donut.HitTest(e.GetPosition(Donut));
    }

    private async void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_pinned) return;
        var index = Donut.HitTest(e.GetPosition(Donut));
        if (index >= 0 && Donut.Items[index].Id == MoreId)
        {
            NextPage();
            Donut.Highlighted = index;
            return;
        }
        var files = _pendingFiles;
        HideDonut();
        if (index >= 0) await StartAsync(files, Donut.Items[index].Id);
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.PickerTitle,
            AllowMultiple = true,
        });
        var paths = picked.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        if (paths.Count > 0)
        {
            ShowDonut(paths, new Point(Overlay.Bounds.Width / 2, Overlay.Bounds.Height / 2), pinned: true);
        }
    }

    // ------------------------------------------------------------ converting

    private ConversionOptions CurrentOptions() => new()
    {
        Quality = (int)QualitySlider.Value,
        MaxDimension = MaxSizes[Math.Clamp(MaxSizeBox.SelectedIndex, 0, MaxSizes.Length - 1)],
        Grayscale = GrayscaleBox.IsChecked == true,
    };

    private async Task StartAsync(List<string> files, string targetId)
    {
        _busy = true;
        var options = CurrentOptions();
        try
        {
            if (targetId == ZipAllId) await ZipAllAsync(files, options);
            else await ConvertAsync(files, FormatCatalog.ById(targetId)!, options);
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.SomethingWrong(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ConvertAsync(List<string> files, FileFormat target, ConversionOptions options)
    {
        var rows = files
            .Select(f => new RecentItem($"{Path.GetFileName(f.TrimEnd('/', '\\'))} → {target.Label}", Strings.Converting))
            .ToList();
        for (var i = rows.Count - 1; i >= 0; i--) _recent.Insert(0, rows[i]);
        StatusText.Text = Strings.ConvertingTo(files.Count, target.Label);

        // Progress<T> was created on the UI thread, so its callbacks run there too.
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
                row.Detail = Strings.Saved(Path.GetFileName(result.OutputPath)!, via, result.Elapsed.TotalSeconds);
            }
            else
            {
                row.Failed = true;
                row.Detail = Strings.Failed(result.Error ?? "");
            }
        });

        var results = await _engine.ConvertManyAsync(files, target, options, progress);
        var ok = results.Count(r => r.Success);
        StatusText.Text = ok == results.Count
            ? Strings.DoneConverted(ok, target.Label)
            : Strings.PartlyDone(ok, results.Count);
    }

    private async Task ZipAllAsync(List<string> files, ConversionOptions options)
    {
        var zip = FormatCatalog.ById("zip")!;
        var stem = files.Count == 1 ? FormatCatalog.StemOf(files[0]) : "Archive";
        var row = new RecentItem(files.Count == 1 ? $"{Path.GetFileName(files[0].TrimEnd('/', '\\'))} → ZIP" : Strings.ItemsToZip(files.Count), Strings.Zipping);
        _recent.Insert(0, row);

        var output = OutputPaths.Reserve(files[0], zip, options, stem);
        try
        {
            await ArchiveConverter.ZipManyAsync(files, output);
            row.OutputPath = output;
            row.Detail = Strings.SavedPlain(Path.GetFileName(output));
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
}
