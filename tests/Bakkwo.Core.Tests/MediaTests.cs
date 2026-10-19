using System.Diagnostics;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;

namespace Bakkwo.Core.Tests;

/// <summary>Real FFmpeg conversions. Each test returns early when FFmpeg isn't installed.</summary>
public class MediaTests : IDisposable
{
    private static readonly string? Ffmpeg = FfmpegLocator.Find();
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static FileFormat F(string id) => FormatCatalog.ById(id)!;

    private static ConversionEngine Engine() => new(new ConverterRegistry().Add(new FfmpegConverter()));

    private static string Run(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    /// <summary>A 2-second 1920×1080 test clip with a tone.</summary>
    private string MakeClip(string name = "clip.mp4")
    {
        var path = _temp.File(name);
        Run(Ffmpeg!, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=1920x1080:rate=25", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "2", "-c:v", "mpeg4", "-c:a", "aac", path);
        return path;
    }

    private static string Probe(string path, string entries) =>
        Run(Path.Combine(Path.GetDirectoryName(Ffmpeg!)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe"),
            "-v", "error", "-show_entries", entries, "-of", "csv=p=0", path).Trim();

    [Theory]
    [InlineData("mov")]
    [InlineData("mkv")]
    [InlineData("wmv")]
    [InlineData("flv")]
    [InlineData("mpeg")]
    [InlineData("ts")]
    [InlineData("3gp")]
    public async Task Video_converts_to(string target)
    {
        if (Ffmpeg is null) return;
        var result = await Engine().ConvertAsync(MakeClip(), F(target));
        Assert.True(result.Success, result.Error);
        Assert.True(new FileInfo(result.OutputPath!).Length > 1000);
    }

    [Theory]
    [InlineData("aac", "aac")]
    [InlineData("opus", "opus")]
    [InlineData("aiff", "pcm_s16be")]
    [InlineData("alac", "alac")]
    [InlineData("wma", "wmav2")]
    public async Task Video_sound_extracts_to(string target, string codec)
    {
        if (Ffmpeg is null) return;
        var result = await Engine().ConvertAsync(MakeClip(), F(target));
        Assert.True(result.Success, result.Error);
        Assert.Equal(codec, Probe(result.OutputPath!, "stream=codec_name"));
    }

    [Fact]
    public async Task Same_format_shrinks_to_720p_with_a_new_name()
    {
        if (Ffmpeg is null) return;
        var clip = MakeClip();
        var result = await Engine().ConvertAsync(clip, F("mp4"), new ConversionOptions { MaxDimension = 1280 });
        Assert.True(result.Success, result.Error);
        Assert.Equal("clip (720p).mp4", Path.GetFileName(result.OutputPath));
        Assert.Equal("1280,720", Probe(result.OutputPath!, "stream=width,height").Split('\n')[0]);
        Assert.True(File.Exists(clip));
    }

    [Fact]
    public async Task Subtitles_are_extracted_and_converted()
    {
        if (Ffmpeg is null) return;
        var srt = _temp.Write("subs.srt", "1\n00:00:00,500 --> 00:00:01,500\n안녕하세요 Hello\n");
        var mkv = _temp.File("movie.mkv");
        Run(Ffmpeg, "-v", "error", "-i", MakeClip(), "-i", srt, "-map", "0", "-map", "1", "-c", "copy", "-c:s", "srt", mkv);

        var ffmpeg = new FfmpegConverter();
        Assert.True(ffmpeg.HasSubtitles(mkv));
        Assert.False(ffmpeg.HasSubtitles(_temp.File("clip.mp4")));

        var engine = Engine();
        var extracted = await engine.ConvertAsync(mkv, F("vtt"));
        Assert.True(extracted.Success, extracted.Error);
        Assert.Contains("안녕하세요 Hello", await File.ReadAllTextAsync(extracted.OutputPath!));

        var none = await engine.ConvertAsync(_temp.File("clip.mp4"), F("srt"));
        Assert.False(none.Success);
        Assert.Equal("This video has no subtitle track to extract.", none.Error);
    }

    [Fact]
    public void Homebrew_style_ffmpeg_falls_back_and_hides_what_it_cannot_write()
    {
        // Homebrew's ffmpeg: no Vorbis, Theora or AMR encoders.
        var brew = new FfmpegConverter(_temp.Write("ffmpeg", ""),
            ["libx264", "aac", "libmp3lame", "libopus", "libvpx-vp9", "flac", "alac", "pcm_s16le", "pcm_s16be", "wmv2", "wmav2", "flv", "mpeg2video", "mp2", "mpeg4"]);

        Assert.True(brew.CanWrite("ogg"));
        Assert.False(brew.CanWrite("ogv"));
        Assert.False(brew.CanWrite("amr"));
        Assert.Contains("libopus", brew.ArgumentsFor("ogg", ConversionOptions.Default));

        var planner = new RoutePlanner(new ConverterRegistry().Add(brew));
        var targets = planner.TargetsFor(F("mp4")).Select(f => f.Id).ToList();
        Assert.Contains("ogg", targets);
        Assert.DoesNotContain("ogv", targets);
        Assert.DoesNotContain("amr", targets);
        // Video → GIF is an end point, not a way to reach still images.
        Assert.Contains("gif", targets);
        Assert.DoesNotContain("png", new RoutePlanner(new ConverterRegistry().Add(brew).Add(new ImageConverter()))
            .TargetsFor(F("mp4")).Select(f => f.Id));
    }

    [Fact]
    public void TypeScript_files_are_not_mistaken_for_video()
    {
        var typescript = _temp.Write("app.ts", "export const x = 1;\n");
        Assert.Null(FormatCatalog.Detect(typescript));

        var transport = _temp.File("broadcast.ts");
        File.WriteAllBytes(transport, [0x47, 0x40, 0x00, 0x10]);
        Assert.Equal("ts", FormatCatalog.Detect(transport)?.Id);
    }

    [Fact]
    public void New_media_formats_are_detected()
    {
        Assert.Equal("mpeg", FormatCatalog.Detect("a.MPG")?.Id);
        Assert.Equal("3gp", FormatCatalog.Detect("phone.3gp")?.Id);
        Assert.Equal("aiff", FormatCatalog.Detect("song.aif")?.Id);
        Assert.Equal("m4a", FormatCatalog.Detect("song.m4a")?.Id); // not "alac", which shares the extension
        Assert.Equal("vtt", FormatCatalog.Detect("captions.vtt")?.Id);
        Assert.Equal("ass", FormatCatalog.Detect("anime.ssa")?.Id);
    }
}
