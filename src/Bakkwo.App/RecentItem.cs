using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Bakkwo.App.Localization;

namespace Bakkwo.App;

/// <summary>One row in the "Recent" list.</summary>
public sealed class RecentItem : INotifyPropertyChanged
{
    private string _detail;
    private double _progress;
    private bool _isRunning = true;
    private bool _failed;
    private string? _outputPath;

    public RecentItem(string title, string detail)
    {
        Title = title;
        _detail = detail;
        RevealCommand = new RelayCommand(() =>
        {
            if (_outputPath is not null) Shell.Reveal(_outputPath);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public double Progress { get => _progress; set => Set(ref _progress, value); }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (Set(ref _isRunning, value)) OnChanged(nameof(CanReveal));
        }
    }

    public bool Failed { get => _failed; set => Set(ref _failed, value); }

    public string? OutputPath
    {
        get => _outputPath;
        set
        {
            if (Set(ref _outputPath, value)) OnChanged(nameof(CanReveal));
        }
    }

    public bool CanReveal => _outputPath is not null && !_isRunning;

    public string ShowLabel => Strings.Show;

    /// <summary>Re-reads labels after the UI language changes.</summary>
    public void RefreshLabels() => OnChanged(nameof(ShowLabel));

    public ICommand RevealCommand { get; }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

/// <summary>Opens the system file manager with a file selected.</summary>
public static class Shell
{
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path]);
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
                Process.Start("xdg-open", [folder]);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No file manager available; nothing useful to do.
        }
    }
}
