using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Bakkwo.Core.Conversion;

namespace Bakkwo.Core.Converters;

/// <summary>
/// Audio, video and subtitles through the FFmpeg command-line tool. Optional: without FFmpeg these
/// edges aren't in the graph and the app says how to install it.
///
/// FFmpeg builds differ (Homebrew's has no Vorbis or Theora encoder, for example), so the converter
/// asks FFmpeg which encoders it has, picks a fallback where one exists (OGG → Opus) and leaves out
/// targets it can't produce (OGV without Theora, AMR without opencore).
/// </summary>
public sealed partial class FfmpegConverter : ConverterBase
{
    private static readonly string[] VideoInputs = ["mp4", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpeg", "ts", "3gp", "ogv", "vob"];
    private static readonly string[] VideoOutputs = ["mp4", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpeg", "ts", "3gp", "ogv"];
    private static readonly string[] AudioFormats = ["mp3", "m4a", "alac", "aac", "wav", "flac", "ogg", "opus", "aiff", "wma", "amr"];
    private static readonly string[] SubtitleFormats = ["srt", "vtt", "ass"];
    private static readonly string[] SubtitleContainers = ["mp4", "mov", "mkv", "webm", "ts", "ogv"];

    /// <summary>Encoders every normal FFmpeg build has; assumed when the encoder list can't be read.</summary>
    private static readonly string[] CommonEncoders =
        ["libx264", "mpeg4", "aac", "alac", "flac", "pcm_s16le", "pcm_s16be", "libmp3lame", "libopus", "libvpx-vp9",
         "wmv2", "wmav2", "flv", "mpeg2video", "mp2", "gif", "srt", "webvtt", "ass"];

    private readonly Lazy<string?> _ffmpeg;
    private readonly Lazy<IReadOnlySet<string>> _encoders;

    public FfmpegConverter(string? ffmpegPath = null, IEnumerable<string>? encoders = null)
    {
        _ffmpeg = new Lazy<string?>(() => ffmpegPath is null
            ? FfmpegLocator.Find()
            : File.Exists(ffmpegPath) ? ffmpegPath : null);
        _encoders = new Lazy<IReadOnlySet<string>>(() => encoders?.ToHashSet() ?? ReadEncoders(_ffmpeg.Value));
    }

    public override string Name => "FFmpeg";

    public override bool IsAvailable => _ffmpeg.Value is not null;

    public override string? UnavailableReason => IsAvailable
        ? null
        : "FFmpeg not found — install it (macOS: brew install ffmpeg) to convert audio and video.";

    // FFmpeg already uses every core, so only a couple of media jobs at once.
    public override int? MaxConcurrency => 2;

    private bool Has(string encoder) => _encoders.Value.Contains(encoder);

    /// <summary>Whether this FFmpeg can write <paramref name="target"/> at all.</summary>
    public bool CanWrite(string target) => target switch
    {
        "ogv" => Has("libtheora") && (Has("libvorbis") || Has("vorbis")),
        "ogg" => Has("libvorbis") || Has("libopus") || Has("vorbis"),
        "opus" => Has("libopus") || Has("opus"),
        "amr" => Has("libopencore_amrnb"),
        "wmv" => Has("wmv2") && Has("wmav2"),
        "wma" => Has("wmav2"),
        "flv" => Has("flv"),
        "mpeg" => Has("mpeg2video") && Has("mp2"),
        "webm" => Has("libvpx-vp9") || Has("libvpx"),
        _ => true,
    };

    public override IEnumerable<ConversionEdge> Edges
    {
        get
        {
            foreach (var from in VideoInputs)
            {
                // Includes from == to: re-encode in the same format to shrink (Max size) or compress (Quality).
                foreach (var to in VideoOutputs.Where(CanWrite)) yield return Edge(from, to);
                foreach (var to in AudioFormats.Where(CanWrite)) yield return Edge(from, to);
                yield return Edge(from, "gif", terminal: true); // an animated GIF, not a route to stills
                // Subtitle tracks only live in some containers; the ring checks the actual file (HasSubtitles).
                if (SubtitleContainers.Contains(from))
                    foreach (var to in SubtitleFormats) yield return Edge(from, to);
            }
            foreach (var from in AudioFormats)
                foreach (var to in AudioFormats.Where(t => t != from && CanWrite(t))) yield return Edge(from, to);
            foreach (var from in SubtitleFormats)
                foreach (var to in SubtitleFormats.Where(t => t != from)) yield return Edge(from, to);
            // No GIF → video edge: the planner would chain any still image through GIF into a "video".
        }
    }

    /// <summary>A subtitle stream: <see cref="Number"/> is its position among subtitle streams (ffmpeg's 0:s:N).</summary>
    public sealed record SubtitleTrack(int Number, string Codec);

    /// <summary>What happens to subtitles when a video is converted: tracks kept (with their output codec) or one burned in.</summary>
    internal sealed record SubtitlePlan(IReadOnlyList<(int Number, string OutputCodec)> Keep, int? BurnIn)
    {
        public static SubtitlePlan None { get; } = new([], null);
    }

    private static readonly HashSet<string> TextSubtitleCodecs = ["subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text"];
    private static readonly HashSet<string> PictureSubtitleCodecs = ["dvd_subtitle", "dvb_subtitle", "hdmv_pgs_subtitle", "xsub"];

    /// <summary>The subtitle streams in a file, in order (empty if there are none or ffprobe is missing).</summary>
    public IReadOnlyList<SubtitleTrack> SubtitleTracks(string path)
    {
        var ffprobe = FfprobePath();
        if (ffprobe is null) return [];
        try
        {
            var info = new ProcessStartInfo(ffprobe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // stream=index,codec_name: MPEG-TS files list each stream again under their program, so de-duplicate by index.
            foreach (var arg in new[] { "-v", "error", "-select_streams", "s", "-show_entries", "stream=index,codec_name", "-of", "csv=p=0", path })
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return [];
            }
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 2 && int.TryParse(parts[0], out _))
                .DistinctBy(parts => parts[0])
                .OrderBy(parts => int.Parse(parts[0]))
                .Select((parts, i) => new SubtitleTrack(i, parts[1]))
                .ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Keeps subtitles wherever the target can hold them: text subtitles become a switchable track
    /// (MP4/MOV: mov_text, WebM: WebVTT, MKV: SRT or ASS), picture subtitles (DVB, DVD, Blu-ray) are
    /// copied into MKV/TS. When the target can't hold picture subtitles (MP4, MOV, WebM) and there's
    /// no text track, the first picture track is burned into the video so it isn't lost.
    /// </summary>
    internal static SubtitlePlan PlanSubtitles(string target, IReadOnlyList<SubtitleTrack> tracks)
    {
        var text = tracks.Where(t => TextSubtitleCodecs.Contains(t.Codec)).ToList();
        var pictures = tracks.Where(t => PictureSubtitleCodecs.Contains(t.Codec)).ToList();

        switch (target)
        {
            case "mp4" or "mov" or "webm":
                var codec = target == "webm" ? "webvtt" : "mov_text";
                if (text.Count > 0) return new SubtitlePlan(text.Select(t => (t.Number, codec)).ToList(), null);
                return pictures.Count > 0 ? new SubtitlePlan([], pictures[0].Number) : SubtitlePlan.None;
            case "mkv":
                return new SubtitlePlan(
                    text.Select(t => (t.Number, t.Codec is "ass" or "ssa" ? "ass" : "srt"))
                        .Concat(pictures.Select(t => (t.Number, "copy")))
                        .OrderBy(t => t.Number)
                        .ToList(),
                    null);
            case "ts":
                return new SubtitlePlan(pictures.Where(t => t.Codec == "dvb_subtitle").Select(t => (t.Number, "copy")).ToList(), null);
            default:
                return SubtitlePlan.None;
        }
    }

    /// <summary>True if the file has at least one subtitle track (uses ffprobe next to ffmpeg).</summary>
    public bool HasSubtitles(string path)
    {
        var ffprobe = FfprobePath();
        if (ffprobe is null) return true; // can't tell; let the conversion explain if there are none
        try
        {
            var info = new ProcessStartInfo(ffprobe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-v", "error", "-select_streams", "s", "-show_entries", "stream=index", "-of", "csv=p=0", path })
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return true;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return true;
            }
            return output.Trim().Length > 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return true;
        }
    }

    private string? FfprobePath()
    {
        if (_ffmpeg.Value is not { } ffmpeg) return null;
        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", name);
        return File.Exists(probe) ? probe : null;
    }

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        var ffmpeg = _ffmpeg.Value ?? throw new InvalidOperationException(UnavailableReason);

        var info = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-y", "-i", step.InputPath }) info.ArgumentList.Add(arg);
        var subtitles = step.To.Id is "mp4" or "mov" or "mkv" or "webm" or "ts"
            ? PlanSubtitles(step.To.Id, SubtitleTracks(step.InputPath))
            : SubtitlePlan.None;
        foreach (var arg in ArgumentsFor(step.To.Id, options, subtitles)) info.ArgumentList.Add(arg);
        foreach (var arg in new[] { "-progress", "pipe:1", "-nostats", step.OutputPath }) info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        using var cancel = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });

        long durationMicros = 0;
        var errorTail = new Queue<string>();

        var readErrors = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (Interlocked.Read(ref durationMicros) == 0 && DurationPattern().Match(line) is { Success: true } m)
                {
                    var seconds = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
                        + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                    Interlocked.Exchange(ref durationMicros, (long)(seconds * 1_000_000));
                }
                lock (errorTail)
                {
                    errorTail.Enqueue(line);
                    if (errorTail.Count > 12) errorTail.Dequeue();
                }
            }
        });

        var readProgress = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                // "out_time_us=" is microseconds; older builds call the same value "out_time_ms".
                if (!line.StartsWith("out_time_us=") && !line.StartsWith("out_time_ms=")) continue;
                var total = Interlocked.Read(ref durationMicros);
                if (total > 0 && long.TryParse(line.AsSpan(line.IndexOf('=') + 1), out var done))
                {
                    progress?.Report(Math.Clamp((double)done / total, 0, 0.99));
                }
            }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(readErrors, readProgress);
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            string detail;
            lock (errorTail) detail = string.Join("\n", errorTail);
            if (detail.Contains("matches no streams", StringComparison.Ordinal) && step.To.Category == Formats.FormatCategory.Subtitle)
                throw new InvalidOperationException("This video has no subtitle track to extract.");
            if (detail.Contains("does not contain any stream", StringComparison.Ordinal) && step.To.Category == Formats.FormatCategory.Audio)
                throw new InvalidOperationException("This video has no sound to extract.");
            throw new InvalidOperationException($"FFmpeg failed (exit code {process.ExitCode}).\n{detail}");
        }
        progress?.Report(1);
    }

    internal IReadOnlyList<string> ArgumentsFor(string target, ConversionOptions options, SubtitlePlan? subtitles = null)
    {
        subtitles ??= SubtitlePlan.None;
        // Quality 100 → CRF 18 (near-lossless), quality 10 → CRF 33 (small).
        var quality = Math.Clamp(options.Quality, 1, 100);
        var crf = Math.Clamp((int)Math.Round(35 - quality * 0.17), 18, 34).ToString(CultureInfo.InvariantCulture);
        var videoFilter = Picture(options.MaxDimension, subtitles);
        var h264 = Has("libx264")
            ? new[] { "-c:v", "libx264", "-preset", "medium", "-crf", crf, "-pix_fmt", "yuv420p" }
            : new[] { "-c:v", "mpeg4", "-q:v", "4" };
        var aac = new[] { "-c:a", "aac", "-b:a", "160k" };
        var mp3Quality = quality >= 90 ? "0" : quality >= 60 ? "2" : "5"; // LAME VBR: 0 ≈ 245 kb/s, 2 ≈ 190, 5 ≈ 130

        return target switch
        {
            // ---------------------------------------------------------------- video
            "mp4" => [.. videoFilter, .. h264, .. aac, "-movflags", "+faststart"],
            "mov" => [.. videoFilter, .. h264, .. aac],
            "mkv" => [.. videoFilter, .. h264, .. aac],
            "ts" => [.. videoFilter, .. h264, .. aac, "-f", "mpegts"],
            "webm" => [.. videoFilter, "-c:v", Has("libvpx-vp9") ? "libvpx-vp9" : "libvpx", "-crf", crf, "-b:v", "0",
                       .. OpusOrVorbis()],
            "avi" => [.. videoFilter, "-c:v", "mpeg4", "-q:v", "4", .. Mp3OrAac(mp3Quality)],
            "wmv" => [.. videoFilter, "-c:v", "wmv2", "-q:v", "4", "-c:a", "wmav2", "-b:a", "160k"],
            "flv" => [.. videoFilter, "-c:v", "flv", "-q:v", "4", .. Mp3OrAac(mp3Quality), "-ar", "44100"],
            "mpeg" => [.. videoFilter, "-c:v", "mpeg2video", "-q:v", "3", "-c:a", "mp2", "-b:a", "192k", "-f", "mpeg"],
            "3gp" => ["-sn", "-vf", ScaleFilter(Math.Min(options.MaxDimension ?? 640, 640)),
                      .. (Has("libx264") ? new[] { "-c:v", "libx264", "-profile:v", "baseline", "-level", "3.0", "-crf", crf, "-pix_fmt", "yuv420p" } : h264),
                      "-c:a", "aac", "-b:a", "64k", "-ac", "1", "-ar", "22050"],
            "ogv" => [.. videoFilter, "-c:v", "libtheora", "-q:v", "7", .. VorbisOrOpus()],
            "gif" => ["-an", "-sn", "-vf", $"fps=12,scale='min({Math.Min(options.MaxDimension ?? 480, 480)},iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse",
                      "-loop", "0"],

            // ---------------------------------------------------------------- audio
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", mp3Quality],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "alac" => ["-vn", "-c:a", "alac", "-f", "ipod"],
            "aac" => ["-vn", "-c:a", "aac", "-b:a", "192k", "-f", "adts"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "flac" => ["-vn", "-c:a", "flac"],
            "aiff" => ["-vn", "-c:a", "pcm_s16be", "-f", "aiff"],
            "ogg" => ["-vn", .. VorbisOrOpus(), "-f", "ogg"],
            "opus" => ["-vn", "-c:a", Has("libopus") ? "libopus" : "opus", "-b:a", "128k", .. (Has("libopus") ? Array.Empty<string>() : ["-strict", "-2"]), "-f", "opus"],
            "wma" => ["-vn", "-c:a", "wmav2", "-b:a", "192k", "-f", "asf"],
            "amr" => ["-vn", "-c:a", "libopencore_amrnb", "-ar", "8000", "-ac", "1", "-b:a", "12.2k"],

            // ---------------------------------------------------------------- subtitles (first subtitle track)
            "srt" => ["-map", "0:s:0", "-c:s", "srt"],
            "vtt" => ["-map", "0:s:0", "-c:s", "webvtt", "-f", "webvtt"],
            "ass" => ["-map", "0:s:0", "-c:s", "ass"],

            _ => throw new NotSupportedException($"FFmpeg target {target} is not configured."),
        };
    }

    /// <summary>
    /// Stream selection and the picture filter. Without a subtitle plan: default streams, no subtitles.
    /// With one: first video, every audio track, and the chosen subtitle tracks, or one burned in.
    /// </summary>
    private static List<string> Picture(int? maxDimension, SubtitlePlan subtitles)
    {
        var scale = ScaleFilter(maxDimension);
        if (subtitles.BurnIn is int burn)
        {
            return ["-filter_complex", $"[0:V:0][0:s:{burn}]overlay=eof_action=pass,{scale}[v]", "-map", "[v]", "-map", "0:a?"];
        }
        if (subtitles.Keep.Count == 0)
        {
            return ["-sn", "-vf", scale];
        }

        var args = new List<string> { "-map", "0:V:0", "-map", "0:a?" };
        foreach (var (number, _) in subtitles.Keep) args.AddRange(["-map", $"0:s:{number}"]);
        args.AddRange(["-vf", scale]);
        for (var i = 0; i < subtitles.Keep.Count; i++) args.AddRange([$"-c:s:{i}", subtitles.Keep[i].OutputCodec]);
        return args;
    }

    /// <summary>Shrinks so neither side exceeds <paramref name="max"/> (never enlarges) and keeps sizes even, which H.264 needs.</summary>
    private static string ScaleFilter(int? max) => max is int m and > 0
        ? $"scale='min({m},iw)':'min({m},ih)':force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2"
        : "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    private string[] OpusOrVorbis() => Has("libopus")
        ? ["-c:a", "libopus", "-b:a", "128k"]
        : Has("libvorbis") ? ["-c:a", "libvorbis", "-q:a", "5"] : ["-c:a", "vorbis", "-strict", "-2", "-ac", "2"];

    private string[] VorbisOrOpus() => Has("libvorbis")
        ? ["-c:a", "libvorbis", "-q:a", "5"]
        : Has("libopus") ? ["-c:a", "libopus", "-b:a", "128k"] : ["-c:a", "vorbis", "-strict", "-2", "-ac", "2"];

    private string[] Mp3OrAac(string mp3Quality) => Has("libmp3lame")
        ? ["-c:a", "libmp3lame", "-q:a", mp3Quality]
        : ["-c:a", "aac", "-b:a", "160k"];

    /// <summary>Runs "ffmpeg -encoders" once and returns the encoder names.</summary>
    private static IReadOnlySet<string> ReadEncoders(string? ffmpeg)
    {
        if (ffmpeg is null) return new HashSet<string>();
        try
        {
            var info = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-hide_banner");
            info.ArgumentList.Add("-encoders");
            using var process = Process.Start(info);
            if (process is null) return CommonEncoders.ToHashSet();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            var names = EncoderLine().Matches(output).Select(m => m.Groups[1].Value).ToHashSet();
            return names.Count > 0 ? names : CommonEncoders.ToHashSet();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return CommonEncoders.ToHashSet();
        }
    }

    [GeneratedRegex(@"^\s*[VAS][F.][S.][X.][B.][D.]\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex EncoderLine();

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationPattern();
}

public static class FfmpegLocator
{
    /// <summary>
    /// Looks in BAKKWO_FFMPEG, then PATH, then the usual install folders. Apps started from
    /// Finder don't inherit the shell's PATH, so Homebrew's folders are checked explicitly.
    /// </summary>
    public static string? Find()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BAKKWO_FFMPEG");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var known = new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/snap/bin" };

        return pathDirs.Concat(known)
            .Select(dir => Path.Combine(dir, exe))
            .FirstOrDefault(File.Exists);
    }
}
