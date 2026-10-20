using Bakkwo.Core.Conversion;

namespace Bakkwo.Mobile;

/// <summary>What the phone screen needs from Android (or iOS): native converters, sharing and saving.</summary>
public interface IPhonePlatform
{
    /// <summary>Converters that use the phone's own decoders, e.g. HEIC photos and video → audio.</summary>
    IEnumerable<IConverter> ExtraConverters { get; }

    /// <summary>A private folder for copies of picked files and for results until they're saved or shared.</summary>
    string WorkDirectory { get; }

    /// <summary>Opens the system share sheet for these files.</summary>
    Task ShareAsync(IReadOnlyList<string> paths);

    /// <summary>
    /// Saves a copy where the user finds it (Android: Download/Bakkwo) and returns that place's name,
    /// or null when this phone can't, in which case the app asks where to save instead.
    /// </summary>
    Task<string?> SaveToDownloadsAsync(string path);
}

/// <summary>Used by previews and tests: no native converters, nothing to share with.</summary>
public sealed class NoPhonePlatform : IPhonePlatform
{
    public IEnumerable<IConverter> ExtraConverters => [];
    public string WorkDirectory { get; } = Path.Combine(Path.GetTempPath(), "Bakkwo-phone");
    public Task ShareAsync(IReadOnlyList<string> paths) => Task.CompletedTask;
    public Task<string?> SaveToDownloadsAsync(string path) => Task.FromResult<string?>(null);
}
