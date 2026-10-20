using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Bakkwo.Mobile;

/// <summary>One row in the results list: a conversion that is running, finished (Save / Share) or failed.</summary>
public sealed class ResultItem : INotifyPropertyChanged
{
    private string _detail;
    private double _progress;
    private bool _isRunning = true;
    private string? _outputPath;
    private bool _failed;

    public ResultItem(string title, string detail, Func<ResultItem, Task> save, Func<ResultItem, Task> share)
    {
        Title = title;
        _detail = detail;
        SaveCommand = new Command(() => save(this));
        ShareCommand = new Command(() => share(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public double Progress { get => _progress; set => Set(ref _progress, value); }

    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

    public bool Failed { get => _failed; set => Set(ref _failed, value); }

    public string? OutputPath
    {
        get => _outputPath;
        set
        {
            Set(ref _outputPath, value);
            OnChanged(nameof(IsDone));
        }
    }

    /// <summary>Finished with a file to save or share.</summary>
    public bool IsDone => _outputPath is not null;

    public ICommand SaveCommand { get; }

    public ICommand ShareCommand { get; }

    // Labels are properties so the buttons follow a language switch.
    public string SaveLabel => UI.Localization.Strings.Save;

    public string ShareLabel => UI.Localization.Strings.Share;

    public void RefreshLabels()
    {
        OnChanged(nameof(SaveLabel));
        OnChanged(nameof(ShareLabel));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(name);
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class Command(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
