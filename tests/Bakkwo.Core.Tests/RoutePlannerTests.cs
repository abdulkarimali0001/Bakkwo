using Bakkwo.Core.Conversion;
using Bakkwo.Core.Formats;

namespace Bakkwo.Core.Tests;

public class RoutePlannerTests
{
    // No FFmpeg here, so results don't depend on what the test machine has installed.
    private static readonly RoutePlanner Planner = new(new ConverterRegistry()
        .Add(new Converters.ImageConverter())
        .Add(new Converters.ImageToPdfConverter())
        .Add(new Converters.DataConverter())
        .Add(new Converters.TextConverter())
        .Add(new Converters.ArchiveConverter()));

    private static FileFormat F(string id) => FormatCatalog.ById(id)!;

    [Fact]
    public void Direct_conversion_is_one_step()
    {
        var route = Planner.FindRoute(F("jpg"), F("png"));
        Assert.NotNull(route);
        Assert.Single(route);
    }

    [Fact]
    public void Yaml_to_csv_goes_through_json()
    {
        var route = Planner.FindRoute(F("yaml"), F("csv"))!;
        Assert.Equal(new[] { "yaml", "json", "csv" }, new[] { route[0].From }.Concat(route.Select(e => e.To)));
    }

    [Fact]
    public void Markdown_to_text_goes_through_html()
    {
        var route = Planner.FindRoute(F("md"), F("txt"))!;
        Assert.Equal(2, route.Count);
        Assert.Equal("html", route[0].To);
    }

    [Fact]
    public void Impossible_route_is_null()
    {
        Assert.Null(Planner.FindRoute(F("pdf"), F("jpg")));
        Assert.Null(Planner.FindRoute(F("json"), F("json"))); // nothing re-encodes JSON in place
    }

    [Fact]
    public void Targets_list_same_category_first()
    {
        var targets = Planner.TargetsFor(F("jpg"));
        Assert.Equal(FormatCategory.Image, targets[0].Category);
        Assert.Contains(targets, f => f.Id == "pdf");
        Assert.DoesNotContain(targets, f => f.Id == "jpg");
    }

    [Fact]
    public void Common_targets_are_the_intersection()
    {
        var common = Planner.CommonTargets([F("jpg"), F("png")]).Select(f => f.Id).ToList();
        Assert.Contains("webp", common);
        Assert.Contains("pdf", common);
        Assert.DoesNotContain("json", common);
        Assert.DoesNotContain("png", common); // same-format re-encoding is only offered when all files share it
    }

    [Fact]
    public void Same_format_is_offered_for_resizing_images()
    {
        Assert.Single(Planner.FindRoute(F("png"), F("png"))!);
        var targets = Planner.CommonTargets([F("png"), F("png")]);
        Assert.Equal("png", targets[^1].Id);
    }

    [Fact]
    public void Max_steps_limits_chains()
    {
        var shortPlanner = new RoutePlanner(new ConverterRegistry().Add(new Converters.DataConverter())) { MaxSteps = 1 };
        Assert.Null(shortPlanner.FindRoute(F("yaml"), F("csv")));
    }

    [Theory]
    [InlineData("photo.JPG", "jpg")]
    [InlineData("backup.tar.gz", "tgz")]
    [InlineData("config.yml", "yaml")]
    [InlineData("notes.markdown", "md")]
    public void Detects_formats_by_extension(string name, string expected)
    {
        Assert.Equal(expected, FormatCatalog.Detect(name)?.Id);
    }

    [Theory]
    [InlineData("jpeg", "jpg")]
    [InlineData(".YML", "yaml")]
    [InlineData("tar.gz", "tgz")]
    public void Resolves_aliases(string alias, string expected)
    {
        Assert.Equal(expected, FormatCatalog.ById(alias)?.Id);
    }

    [Fact]
    public void Stem_strips_double_extensions()
    {
        Assert.Equal("logs", FormatCatalog.StemOf("/tmp/logs.tar.gz"));
        Assert.Equal("my.photo", FormatCatalog.StemOf("my.photo.png"));
    }
}
