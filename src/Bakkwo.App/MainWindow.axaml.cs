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
    private static readonly int?[] MaxSizes = [null, 3840, 1920, 1080, 512];

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

        var mediaReady = _engine.Registry.Converters.All(c => c.IsAvailable);
        EngineText.Text = mediaReady ? Strings.AllReady : Strings.FfmpegMissing;

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
            Donut.Highlighted = Donut.HitTest(e.GetPosition(Donut));
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
        var items = new List<DonutItem>();

        if (formats.All(f => f is not null))
        {
            items.AddRange(_engine.Planner.CommonTargets(formats.Select(f => f!))
                .Select(f => new DonutItem(f.Id, f.IsFolder ? Strings.Extract : f.Label)));
        }
        if (items.All(i => i.Id != "zip"))
        {
            items.Add(new DonutItem(ZipAllId, files.Count > 1 ? Strings.ZipAll : "ZIP"));
        }

        _pendingFiles = files;
        Donut.Items = items;
        Donut.CenterText = files.Count == 1 ? Path.GetFileName(files[0].TrimEnd('/', '\\')) : Strings.FilesCount(files.Count);
        Donut.Highlighted = DonutMenu.NoIndex;
        PlaceDonut(at);
        Donut.IsVisible = true;

        _pinned = pinned;
        Overlay.IsHitTestVisible = pinned;
        StatusText.Text = formats.Any(f => f is null)
            ? Strings.UnknownTypes
            : pinned ? Strings.ClickFormat : Strings.ReleaseOverFormat;
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
