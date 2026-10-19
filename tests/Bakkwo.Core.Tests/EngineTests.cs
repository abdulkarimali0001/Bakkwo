using System.Text;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakkwo.Core.Tests;

public class EngineTests : IDisposable
{
    private readonly TempFolder _temp = new();

    // FFmpeg pointed at a missing path, so audio/video edges are simply unavailable.
    private readonly ConversionEngine _engine = new(new ConverterRegistry()
        .Add(new ImageConverter())
        .Add(new ImageToPdfConverter())
        .Add(new DataConverter())
        .Add(new TextConverter())
        .Add(new ArchiveConverter())
        .Add(new FfmpegConverter("/nonexistent/ffmpeg")));

    public void Dispose() => _temp.Dispose();

    private string MakePng(string name, int width = 40, int height = 30)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 80, 0, 128));
        var path = _temp.File(name);
        image.SaveAsPng(path);
        return path;
    }

    private static FileFormat F(string id) => FormatCatalog.ById(id)!;

    [Theory]
    [InlineData("jpg")]
    [InlineData("webp")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("tiff")]
    public async Task Converts_png_to_other_images(string target)
    {
        var png = MakePng("pic.png");
        var result = await _engine.ConvertAsync(png, F(target));

        Assert.True(result.Success, result.Error);
        Assert.Equal(_temp.File("pic" + F(target).PrimaryExtension), result.OutputPath);
        using var loaded = await Image.LoadAsync(result.OutputPath!);
        Assert.Equal(40, loaded.Width);
    }

    [Fact]
    public async Task Max_dimension_shrinks_but_keeps_aspect()
    {
        var png = MakePng("big.png", 400, 200);
        var result = await _engine.ConvertAsync(png, F("jpg"), new ConversionOptions { MaxDimension = 100 });
        using var loaded = await Image.LoadAsync(result.OutputPath!);
        Assert.Equal(100, loaded.Width);
        Assert.Equal(50, loaded.Height);
    }

    [Fact]
    public async Task Writes_a_valid_ico()
    {
        var result = await _engine.ConvertAsync(MakePng("icon.png", 64, 64), F("ico"));
        var bytes = await File.ReadAllBytesAsync(result.OutputPath!);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, bytes[..4]);
        Assert.Equal(5, BitConverter.ToUInt16(bytes, 4)); // 16, 24, 32, 48, 64
    }

    [Fact]
    public async Task Writes_a_valid_pdf()
    {
        var result = await _engine.ConvertAsync(MakePng("page.png"), F("pdf"));
        var text = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(result.OutputPath!));
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Filter /DCTDecode", text);
        Assert.EndsWith("%%EOF\n", text);

        // startxref must point at the "xref" keyword.
        var startxref = long.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.Equal("xref", text.Substring((int)startxref, 4));
    }

    [Fact]
    public async Task Merges_images_into_one_pdf()
    {
        var output = _temp.File("album.pdf");
        await ImageToPdfConverter.MergeAsync([MakePng("a.png"), MakePng("b.png", 30, 40)], output, ConversionOptions.Default);
        var text = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output));
        Assert.Contains("/Count 2", text);
    }

    [Fact]
    public async Task Multi_step_route_cleans_up_and_reports_route()
    {
        var yaml = _temp.Write("people.yaml", "- name: Ali\n  city: Sokcho\n- name: Kim\n  city: Seoul\n");
        var result = await _engine.ConvertAsync(yaml, F("csv"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("yaml → json → csv", result.RouteDescription);
        var csv = await File.ReadAllTextAsync(result.OutputPath!);
        Assert.Contains("Ali,Sokcho", csv);
        Assert.Equal(2, Directory.GetFiles(_temp.Path).Length); // no stray temp files next to the input
    }

    [Fact]
    public async Task Existing_output_is_never_overwritten()
    {
        var json = _temp.Write("data.json", "[{\"a\":1}]");
        _temp.Write("data.csv", "keep me");

        var result = await _engine.ConvertAsync(json, F("csv"));
        Assert.Equal(_temp.File("data (1).csv"), result.OutputPath);
        Assert.Equal("keep me", await File.ReadAllTextAsync(_temp.File("data.csv")));
    }

    [Fact]
    public async Task Failure_reports_error_and_leaves_no_placeholder()
    {
        var broken = _temp.Write("broken.json", "{ not json");
        var result = await _engine.ConvertAsync(broken, F("yaml"));

        Assert.False(result.Success);
        Assert.False(File.Exists(_temp.File("broken.yaml")));
    }

    [Fact]
    public async Task Unsupported_route_fails_cleanly()
    {
        var result = await _engine.ConvertAsync(_temp.Write("a.txt", "hi"), F("mp3"));
        Assert.False(result.Success);
        Assert.Contains("Can't convert", result.Error);
    }

    [Fact]
    public async Task Batch_keeps_input_order_and_unique_names()
    {
        var inputs = new[] { MakePng("x.png"), MakePng("y.png"), MakePng("z.png") };
        var results = await _engine.ConvertManyAsync(inputs, F("webp"));
        Assert.All(results, r => Assert.True(r.Success, r.Error));
        Assert.Equal(new[] { "x.webp", "y.webp", "z.webp" }, results.Select(r => Path.GetFileName(r.OutputPath!)));
    }

    [Fact]
    public async Task Markdown_becomes_html_then_text()
    {
        var md = _temp.Write("readme.md", "# Hello\n\nSome **bold** text.\n\n- one\n- two\n");
        var html = await _engine.ConvertAsync(md, F("html"));
        Assert.Contains("<strong>bold</strong>", await File.ReadAllTextAsync(html.OutputPath!));

        var txt = await _engine.ConvertAsync(md, F("txt"));
        var text = await File.ReadAllTextAsync(txt.OutputPath!);
        Assert.Contains("Some bold text.", text);
        Assert.Contains("• one", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public async Task Archives_round_trip_through_tgz()
    {
        var folder = Directory.CreateDirectory(_temp.File("project")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.txt"), "alpha");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "beta");

        var zip = _temp.File("project.zip");
        await ArchiveConverter.ZipManyAsync([folder], zip);

        var tgz = await _engine.ConvertAsync(zip, F("tgz"));
        Assert.True(tgz.Success, tgz.Error);
        Assert.EndsWith("project.tar.gz", tgz.OutputPath);

        var extracted = await _engine.ConvertAsync(tgz.OutputPath!, F("folder"));
        Assert.True(extracted.Success, extracted.Error);
        Assert.Equal("beta", File.ReadAllText(Path.Combine(extracted.OutputPath!, "project", "sub", "b.txt")));
    }

    [Fact]
    public void Markdown_lists_become_single_spaced_bullets()
    {
        var text = Converters.TextConverter.HtmlToText("<ul>\n<li>one</li>\n<li>two</li>\n</ul>\n<p>after</p>");
        Assert.Equal("• one\n• two\n\nafter\n", text);
    }

    [Fact]
    public void Still_images_never_route_to_video_even_with_ffmpeg()
    {
        // Point FFmpeg at a file that exists so its edges are active.
        var fake = _temp.Write("ffmpeg", "");
        var planner = new RoutePlanner(new ConverterRegistry().Add(new ImageConverter()).Add(new FfmpegConverter(fake)));
        var targets = planner.TargetsFor(F("png")).Select(f => f.Category).ToHashSet();
        Assert.DoesNotContain(FormatCategory.Video, targets);
        Assert.DoesNotContain(FormatCategory.Audio, targets);
        Assert.NotNull(planner.FindRoute(F("mp4"), F("gif")));
    }

    [Fact]
    public void Missing_ffmpeg_hides_media_routes()
    {
        Assert.Null(_engine.Planner.FindRoute(F("mp4"), F("mp3")));
        Assert.Contains(_engine.Registry.Converters, c => !c.IsAvailable && c.UnavailableReason!.Contains("FFmpeg"));
    }
}
