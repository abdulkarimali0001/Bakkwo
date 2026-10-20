using System.Text.Json;
using Bakkwo.UI;
using Bakkwo.UI.Localization;

namespace Bakkwo.UI;

/// <summary>A few preferences, kept in a small JSON file in the user's app-data folder.</summary>
public sealed class Settings
{
    public string? Language { get; set; }
    public int Quality { get; set; } = 90;
    public int MaxSizeIndex { get; set; }
    public bool Grayscale { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bakkwo", "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; never let them break the app.
        }
    }

    public AppLanguage? LanguageChoice => Language switch
    {
        "ko" => AppLanguage.Korean,
        "en" => AppLanguage.English,
        _ => null,
    };
}
