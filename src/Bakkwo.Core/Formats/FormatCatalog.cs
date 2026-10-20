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
        // Phone photos. Read by the phone app's system decoder; the desktop app can only zip them for now.
        new("heic", "HEIC photo", Image, [".heic", ".heif", ".hif"]),
        new("avif", "AVIF image", Image, [".avif"]),

        // Documents
        new("pdf", "PDF document", Document, [".pdf"]),
        new("docx", "Word document", Document, [".docx"]),
        new("doc", "Word 97–2003 document", Document, [".doc"]),
        new("docm", "Word macro-enabled document", Document, [".docm"]),
        new("dotx", "Word template", Document, [".dotx"]),
        new("dot", "Word 97–2003 template", Document, [".dot"]),
        new("rtf", "Rich Text document", Document, [".rtf"]),
        new("odt", "OpenDocument text", Document, [".odt"]),

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
        new("mov", "QuickTime video", Video, [".mov", ".qt"]),
        new("mkv", "Matroska video", Video, [".mkv"]),
        new("webm", "WebM video", Video, [".webm"]),
        new("avi", "AVI video", Video, [".avi"]),
        new("wmv", "Windows Media video", Video, [".wmv", ".asf"]),
        new("flv", "Flash video", Video, [".flv"]),
        new("mpeg", "MPEG video", Video, [".mpg", ".mpeg", ".m2v"]),
        new("ts", "MPEG transport stream", Video, [".ts", ".mts", ".m2ts"]),
        new("3gp", "3GPP mobile video", Video, [".3gp", ".3g2"]),
        new("ogv", "Ogg Theora video", Video, [".ogv"]),
        new("vob", "DVD video", Video, [".vob"]),

        // Audio (needs FFmpeg)
        new("mp3", "MP3 audio", Audio, [".mp3"]),
        new("m4a", "AAC audio (M4A)", Audio, [".m4a"]),
        new("alac", "Apple Lossless", Audio, [".m4a"]),   // written as .m4a; files named .m4a are detected as m4a
        new("aac", "AAC audio", Audio, [".aac"]),
        new("wav", "WAV audio", Audio, [".wav"]),
        new("flac", "FLAC audio", Audio, [".flac"]),
        new("ogg", "Ogg audio", Audio, [".ogg", ".oga"]),
        new("opus", "Opus audio", Audio, [".opus"]),
        new("aiff", "AIFF audio", Audio, [".aiff", ".aif"]),
        new("wma", "Windows Media audio", Audio, [".wma"]),
        new("amr", "AMR voice recording", Audio, [".amr"]),

        // Subtitles (needs FFmpeg)
        new("srt", "SubRip subtitles", Subtitle, [".srt"]),
        new("vtt", "WebVTT subtitles", Subtitle, [".vtt"]),
        new("ass", "ASS subtitles", Subtitle, [".ass", ".ssa"]),

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
        ["mpg"] = "mpeg",
        ["m2ts"] = "ts",
        ["mts"] = "ts",
        ["aif"] = "aiff",
        ["webvtt"] = "vtt",
        ["ssa"] = "ass",
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
        // ".ts" is also TypeScript. MPEG-TS packets start with the sync byte 0x47.
        if (best?.Id == "ts" && name.EndsWith(".ts", StringComparison.Ordinal) && !LooksLikeTransportStream(path)) return null;
        return best;
    }

    /// <summary>
    /// True when the file holds MPEG-TS packets: the 0x47 sync byte repeating every 188 bytes (or every
    /// 192 bytes for camcorder/Blu-ray style packets). Searching the first 64 KB also covers streamed
    /// and downloaded .ts files that start with an ID3 tag or padding. TypeScript source never matches.
    /// </summary>
    internal static bool LooksLikeTransportStream(string path)
    {
        try
        {
            if (!File.Exists(path)) return true; // name-only detection (CLI targets, tests)
            var buffer = new byte[64 * 1024];
            int read;
            using (var stream = File.OpenRead(path)) read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return HasTsSync(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool HasTsSync(ReadOnlySpan<byte> data)
    {
        foreach (var stride in (ReadOnlySpan<int>)[188, 192])
        {
            const int packetsToCheck = 5;
            for (var start = 0; start + stride * (packetsToCheck - 1) < data.Length; start++)
            {
                var ok = true;
                for (var k = 0; k < packetsToCheck && ok; k++) ok = data[start + k * stride] == 0x47;
                if (ok) return true;
            }
        }
        return false;
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
