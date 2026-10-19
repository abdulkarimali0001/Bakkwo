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
    public async Task Real_ts_files_convert_to_mp4()
    {
        if (Ffmpeg is null) return;
        var clip = MakeClip();
        var engine = Engine();

        // A plain transport stream, and an HLS-style one whose packets follow an ID3 tag.
        var plain = _temp.File("plain.ts");
        Run(Ffmpeg, "-v", "error", "-i", clip, "-c:v", "libx264", "-c:a", "aac", "-f", "mpegts", plain);
        var hls = _temp.File("hls.ts");
        File.WriteAllBytes(hls, [.. "ID3"u8, 4, 0, 0, 0, 0, 0, .. new byte[0], .. File.ReadAllBytes(plain)]);

        foreach (var ts in new[] { plain, hls })
        {
            Assert.Equal("ts", FormatCatalog.Detect(ts)?.Id);
            var mp4 = await engine.ConvertAsync(ts, F("mp4"));
            Assert.True(mp4.Success, mp4.Error);
            Assert.StartsWith("h264", Probe(mp4.OutputPath!, "stream=codec_name"));
        }
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
        Assert.True(ffmpeg.HasTextSubtitles(mkv));
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
    public void Subtitle_plan_keeps_text_tracks_and_burns_picture_ones_only_when_needed()
    {
        FfmpegConverter.SubtitleTrack T(int n, string codec) => new(n, codec);
        var text = new[] { T(0, "subrip"), T(1, "ass") };
        var tv = new[] { T(0, "dvb_subtitle") };

        var mp4 = FfmpegConverter.PlanSubtitles("mp4", text);
        Assert.Equal([(0, "mov_text"), (1, "mov_text")], mp4.Keep);
        Assert.Null(mp4.BurnIn);

        Assert.Equal([(0, "webvtt"), (1, "webvtt")], FfmpegConverter.PlanSubtitles("webm", text).Keep);
        Assert.Equal([(0, "srt"), (1, "ass")], FfmpegConverter.PlanSubtitles("mkv", text).Keep);

        Assert.Equal(0, FfmpegConverter.PlanSubtitles("mp4", tv).BurnIn);          // MP4 can't hold DVB: burn in
        Assert.Equal([(0, "copy")], FfmpegConverter.PlanSubtitles("mkv", tv).Keep); // MKV keeps it as a track
        Assert.Equal([(0, "copy")], FfmpegConverter.PlanSubtitles("ts", tv).Keep);
        Assert.Empty(FfmpegConverter.PlanSubtitles("avi", text).Keep);
    }

    [Fact]
    public async Task Text_subtitles_come_along_into_mp4_and_webm()
    {
        if (Ffmpeg is null) return;
        var srt = _temp.Write("subs.srt", "1\n00:00:00,500 --> 00:00:01,500\n안녕하세요 Hello\n");
        var mkv = _temp.File("movie.mkv");
        Run(Ffmpeg, "-v", "error", "-i", MakeClip(), "-i", srt, "-map", "0", "-map", "1", "-c", "copy", "-c:s", "srt", mkv);
        var engine = Engine();

        var mp4 = await engine.ConvertAsync(mkv, F("mp4"));
        Assert.True(mp4.Success, mp4.Error);
        Assert.Contains("mov_text", Probe(mp4.OutputPath!, "stream=codec_name"));

        var webm = await engine.ConvertAsync(mkv, F("webm"));
        Assert.True(webm.Success, webm.Error);
        Assert.Contains("webvtt", Probe(webm.OutputPath!, "stream=codec_name"));
    }

    [Fact]
    public async Task Tv_recording_subtitles_are_kept_in_mkv_and_burned_into_mp4()
    {
        if (Ffmpeg is null) return;
        // A TV-style .ts: H.264 + AAC + DVB picture subtitles (made from a tiny hand-written PGS file).
        var sup = _temp.File("subs.sup");
        File.WriteAllBytes(sup, Pgs.WhiteBox(1920, 1080, x: 760, y: 960, width: 400, height: 60, fromSeconds: 0.5, toSeconds: 1.5));
        var ts = _temp.File("tv.ts");
        Run(Ffmpeg, "-v", "error", "-i", MakeClip(), "-i", sup, "-map", "0:v", "-map", "0:a", "-map", "1",
            "-c:v", "libx264", "-c:a", "aac", "-c:s", "dvb_subtitle", ts);
        Assert.Contains("dvb_subtitle", Probe(ts, "stream=codec_name"));

        var engine = Engine();
        var mkv = await engine.ConvertAsync(ts, F("mkv"));
        Assert.True(mkv.Success, mkv.Error);
        Assert.Contains("dvb_subtitle", Probe(mkv.OutputPath!, "stream=codec_name"));

        // Picture subtitles can't become text: not offered in the ring, and a clear message if asked anyway.
        var ffmpeg = new FfmpegConverter();
        Assert.True(ffmpeg.HasSubtitles(ts));
        Assert.False(ffmpeg.HasTextSubtitles(ts));
        var srt = await engine.ConvertAsync(ts, F("srt"));
        Assert.False(srt.Success);
        Assert.StartsWith("This video's subtitles are pictures", srt.Error);

        // A file with a picture track first and a text track second: extraction takes the text one.
        var mixed = _temp.File("mixed.mkv");
        var textSrt = _temp.Write("text.srt", "1\n00:00:00,500 --> 00:00:01,500\n두 번째 트랙 second track\n");
        Run(Ffmpeg, "-v", "error", "-i", ts, "-i", textSrt, "-map", "0:v", "-map", "0:s", "-map", "1", "-c", "copy", "-c:s:1", "srt", mixed);
        Assert.True(ffmpeg.HasTextSubtitles(mixed));
        var fromMixed = await engine.ConvertAsync(mixed, F("srt"));
        Assert.True(fromMixed.Success, fromMixed.Error);
        Assert.Contains("두 번째 트랙 second track", await File.ReadAllTextAsync(fromMixed.OutputPath!));

        var mp4 = await engine.ConvertAsync(ts, F("mp4"));
        Assert.True(mp4.Success, mp4.Error);
        Assert.DoesNotContain("dvb", Probe(mp4.OutputPath!, "stream=codec_name"));
        // Burned in: the subtitle box is white in the picture while the subtitle is on screen.
        var box = Run(Ffmpeg, "-v", "error", "-ss", "1.0", "-i", mp4.OutputPath!, "-frames:v", "1",
            "-vf", "crop=400:60:760:960,format=gray,scale=1:1:flags=area", "-f", "rawvideo", "-");
        Assert.True(box.Length > 0 && box[0] > 230, $"expected a white subtitle box, got brightness {(box.Length > 0 ? box[0] : -1)}");
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

        // Plain 188-byte packets.
        Assert.Equal("ts", FormatCatalog.Detect(WritePackets("broadcast.ts", prefix: [], packet: 188, offset: 0))?.Id);
        // Streamed/downloaded segments often start with an ID3 tag before the first packet.
        Assert.Equal("ts", FormatCatalog.Detect(WritePackets("segment.ts", prefix: [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 10, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], packet: 188, offset: 0))?.Id);
        // 192-byte packets (4-byte timestamp, then the sync byte).
        Assert.Equal("ts", FormatCatalog.Detect(WritePackets("camera.ts", prefix: [], packet: 192, offset: 4))?.Id);
    }

    private string WritePackets(string name, byte[] prefix, int packet, int offset)
    {
        var data = new byte[prefix.Length + packet * 8];
        prefix.CopyTo(data, 0);
        for (var i = 0; i < 8; i++) data[prefix.Length + i * packet + offset] = 0x47;
        var path = _temp.File(name);
        File.WriteAllBytes(path, data);
        return path;
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
