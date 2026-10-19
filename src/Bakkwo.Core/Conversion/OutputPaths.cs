using Bakkwo.Core.Formats;

namespace Bakkwo.Core.Conversion;

/// <summary>
/// Picks where a result goes ("photo.png" → "photo.webp", or "photo (1).webp" if taken) and claims
/// the name at once with an empty placeholder, so parallel jobs never pick the same file.
/// </summary>
public static class OutputPaths
{
    private static readonly object Gate = new();

    public static string Reserve(string inputPath, FileFormat target, ConversionOptions options, string? stem = null)
    {
        var full = Path.GetFullPath(inputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = options.OutputDirectory ?? Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
        return ReserveIn(directory, stem ?? FormatCatalog.StemOf(full), target);
    }

    public static string ReserveIn(string directory, string stem, FileFormat target)
    {
        Directory.CreateDirectory(directory);
        lock (Gate)
        {
            for (var n = 0; ; n++)
            {
                var name = n == 0 ? stem : $"{stem} ({n})";
                var candidate = Path.Combine(directory, name + target.PrimaryExtension);
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;

                if (target.IsFolder) Directory.CreateDirectory(candidate);
                else File.WriteAllBytes(candidate, []);
                return candidate;
            }
        }
    }
}
