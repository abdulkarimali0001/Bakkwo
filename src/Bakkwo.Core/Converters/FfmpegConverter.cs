using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Bakkwo.Core.Conversion;

namespace Bakkwo.Core.Converters;

/// <summary>
/// Audio and video through the FFmpeg command-line tool. Optional: if FFmpeg isn't installed,
/// these edges simply don't appear in the graph and the app says how to install it.
/// </summary>
public sealed partial class FfmpegConverter : ConverterBase
{
    private static readonly string[] Video = ["mp4", "mov", "mkv", "webm", "avi"];
    private static readonly string[] Audio = ["mp3", "m4a", "wav", "flac", "ogg"];

    private readonly Lazy<string?> _ffmpeg;

    public FfmpegConverter(string? ffmpegPath = null) =>
        _ffmpeg = new Lazy<string?>(() => ffmpegPath is null
            ? FfmpegLocator.Find()
            : File.Exists(ffmpegPath) ? ffmpegPath : null);

    public override string Name => "FFmpeg";

    // FFmpeg already uses every core, so only a couple of media jobs at once.
    public override int? MaxConcurrency => 2;

    public override bool IsAvailable => _ffmpeg.Value is not null;

    public override string? UnavailableReason => IsAvailable
        ? null
        : "FFmpeg not found — install it (macOS: brew install ffmpeg) to convert audio and video.";

    public override IEnumerable<ConversionEdge> Edges
    {
        get
        {
            foreach (var from in Video)
            {
                foreach (var to in Video.Where(t => t != from)) yield return Edge(from, to);
                foreach (var to in Audio) yield return Edge(from, to);
                yield return Edge(from, "gif");
            }
            foreach (var from in Audio)
            {
                foreach (var to in Audio.Where(t => t != from)) yield return Edge(from, to);
            }
            // No GIF → video edge: the planner would chain any still image through GIF into a "video".
        }
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
        foreach (var arg in ArgumentsFor(step.To.Id, options)) info.ArgumentList.Add(arg);
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
            throw new InvalidOperationException($"FFmpeg failed (exit code {process.ExitCode}).\n{detail}");
        }
        progress?.Report(1);
    }

    internal static IReadOnlyList<string> ArgumentsFor(string target, ConversionOptions options)
    {
        // Quality 100 → CRF 18 (near-lossless), quality 10 → CRF 33 (small).
        var crf = Math.Clamp((int)Math.Round(35 - Math.Clamp(options.Quality, 1, 100) * 0.17), 18, 34)
            .ToString(CultureInfo.InvariantCulture);
        var evenSize = new[] { "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2" }; // H.264 needs even dimensions

        return target switch
        {
            "mp4" => [.. evenSize, "-c:v", "libx264", "-preset", "medium", "-crf", crf, "-pix_fmt", "yuv420p",
                      "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"],
            "mov" => [.. evenSize, "-c:v", "libx264", "-crf", crf, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k"],
            "mkv" => [.. evenSize, "-c:v", "libx264", "-crf", crf, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k"],
            "webm" => ["-c:v", "libvpx-vp9", "-crf", crf, "-b:v", "0", "-c:a", "libopus", "-b:a", "128k"],
            "avi" => ["-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-q:a", "3"],
            "gif" => ["-an", "-vf", "fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse",
                      "-loop", "0"],
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "flac" => ["-vn", "-c:a", "flac"],
            "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
            _ => throw new NotSupportedException($"FFmpeg target {target} is not configured."),
        };
    }

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
