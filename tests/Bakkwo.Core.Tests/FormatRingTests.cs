using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;
using Bakkwo.UI.Ring;

namespace Bakkwo.Core.Tests;

/// <summary>The ring's choices, as the desktop and phone apps build them (without FFmpeg or LibreOffice).</summary>
public class FormatRingTests
{
    private static readonly ConversionEngine Engine = new(new ConverterRegistry()
        .Add(new ImageConverter())
        .Add(new ImageToPdfConverter())
        .Add(new DataConverter())
        .Add(new TextConverter())
        .Add(new WordConverter())
        .Add(new ArchiveConverter()));

    private static List<string> Ids(FormatRing ring) => ring.CurrentItems().Select(i => i.Id).ToList();

    [Fact]
    public void Photo_suggestions_come_first_with_smaller_in_third_place()
    {
        var ring = FormatRing.For(["/x/beach.jpg"], Engine);
        Assert.Equal(["png", "webp", "jpg", "pdf", "ico", "gif", FormatRing.MoreId], Ids(ring));
    }

    [Fact]
    public void More_pages_through_everything_and_back()
    {
        var ring = FormatRing.For(["/x/beach.jpg"], Engine, slicesPerPage: 3);
        var seen = new List<string>();
        for (var page = 1; page < ring.PageCount; page++)
        {
            ring.NextPage();
            seen.AddRange(Ids(ring).Where(id => id != FormatRing.MoreId));
        }
        Assert.Contains("bmp", seen);
        Assert.Contains("tiff", seen);
        ring.NextPage();
        Assert.Equal(0, ring.Page);
    }

    [Fact]
    public void Phone_ring_leaves_out_extract_to_folder()
    {
        var desktop = FormatRing.For(["/x/photos.zip"], Engine);
        var phone = FormatRing.For(["/x/photos.zip"], Engine, exclude: new HashSet<string> { "folder" });
        Assert.Contains("folder", Ids(desktop));
        Assert.DoesNotContain("folder", Ids(phone));
    }

    [Fact]
    public void Unknown_files_can_still_be_zipped()
    {
        var ring = FormatRing.For(["/x/thing.xyz"], Engine);
        Assert.True(ring.HasUnknownFiles);
        Assert.Equal([FormatRing.ZipAllId], Ids(ring));
    }

    [Fact]
    public void Heic_is_known_but_needs_the_phone_decoder()
    {
        Assert.Equal("heic", FormatCatalog.Detect("/x/IMG_2041.HEIC")?.Id);
        Assert.Equal([FormatRing.ZipAllId], Ids(FormatRing.For(["/x/IMG_2041.HEIC"], Engine)));
    }
}
