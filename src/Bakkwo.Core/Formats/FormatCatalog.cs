namespace Bakkwo.Core.Formats;

using static FormatCategory;

/// <summary>Every format Bakkwo can read or write, and detection by file name.</summary>
public static class FormatCatalog
{
    private static readonly List<FileFormat> Formats =
    [
        // Images
        new("jpg", "JPEG image", Image, [".jpg", ".jpeg", ".jfif"]),
        new("png", "PNG image", Image, [".png"]),
        new("webp", "WebP image", Image, [".webp"]),
        new("gif", "GIF image", Image, [".gif"]),
        new("bmp", "Bitmap image", Image, [".bmp"]),
        new("tiff", "TIFF image", Image, [".tiff", ".tif"]),
        new("tga", "Targa image", Image, [".tga"]),
        new("ico", "Windows icon", Image, [".ico"]),

        // Documents
        new("pdf", "PDF document", Document, [".pdf"]),

        // Text
        new("md", "Markdown", Text, [".md", ".markdown"]),
        new("html", "HTML page", Text, [".html", ".htm"]),
        new("txt", "Plain text", Text, [".txt"]),

        // Data
        new("json", "JSON", Data, [".json"]),
        new("yaml", "YAML", Data, [".yaml", ".yml"]),
        new("csv", "CSV table", Data, [".csv"]),
        new("tsv", "TSV table", Data, [".tsv"]),
        new("xml", "XML", Data, [".xml"]),

        // Video (needs FFmpeg)
        new("mp4", "MP4 video", Video, [".mp4", ".m4v"]),
        new("mov", "QuickTime video", Video, [".mov"]),
        new("mkv", "Matroska video", Video, [".mkv"]),
        new("webm", "WebM video", Video, [".webm"]),
        new("avi", "AVI video", Video, [".avi"]),

        // Audio (needs FFmpeg)
        new("mp3", "MP3 audio", Audio, [".mp3"]),
        new("m4a", "AAC audio", Audio, [".m4a"]),
        new("wav", "WAV audio", Audio, [".wav"]),
        new("flac", "FLAC audio", Audio, [".flac"]),
        new("ogg", "Ogg Vorbis audio", Audio, [".ogg"]),

        // Archives
        new("zip", "ZIP archive", Archive, [".zip"]),
        new("tar", "TAR archive", Archive, [".tar"]),
        new("tgz", "Gzipped TAR archive", Archive, [".tar.gz", ".tgz"]),
        new("folder", "Folder", Folder, []),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg",
        ["jfif"] = "jpg",
        ["tif"] = "tiff",
        ["yml"] = "yaml",
        ["htm"] = "html",
        ["markdown"] = "md",
        ["text"] = "txt",
        ["tar.gz"] = "tgz",
        ["m4v"] = "mp4",
        ["dir"] = "folder",
    };

    public static IReadOnlyList<FileFormat> All => Formats;

    /// <summary>Finds a format by id or common alias ("jpeg", ".yml", "tar.gz").</summary>
    public static FileFormat? ById(string id)
    {
        var key = id.Trim().TrimStart('.').ToLowerInvariant();
        if (Aliases.TryGetValue(key, out var real)) key = real;
        return Formats.FirstOrDefault(f => f.Id == key);
    }

    /// <summary>Detects a path's format from its extension; directories are <c>folder</c>.</summary>
    public static FileFormat? Detect(string path)
    {
        if (Directory.Exists(path)) return ById("folder");

        var name = Path.GetFileName(path).ToLowerInvariant();
        FileFormat? best = null;
        var bestLength = 0;
        foreach (var format in Formats)
        {
            foreach (var ext in format.Extensions)
            {
                // Longest match wins so "a.tar.gz" is tgz, not something ending in ".gz".
                if (ext.Length > bestLength && name.EndsWith(ext, StringComparison.Ordinal) && name.Length > ext.Length)
                {
                    best = format;
                    bestLength = ext.Length;
                }
            }
        }
        return best;
    }

    /// <summary>The file name without its (possibly double) extension: "logs.tar.gz" → "logs".</summary>
    public static string StemOf(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (Directory.Exists(path)) return name;

        var format = Detect(path);
        if (format is not null)
        {
            var lower = name.ToLowerInvariant();
            var ext = format.Extensions.Where(e => lower.EndsWith(e, StringComparison.Ordinal)).MaxBy(e => e.Length);
            if (ext is not null) return name[..^ext.Length];
        }
        return Path.GetFileNameWithoutExtension(name);
    }
}
