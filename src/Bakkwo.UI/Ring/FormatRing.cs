using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;
using Bakkwo.UI.Controls;
using Bakkwo.UI.Localization;

namespace Bakkwo.UI.Ring;

/// <summary>
/// What the ring offers for a set of files: a first page of suggestions for that kind of file, then every
/// other possible format behind "More". Shared by the desktop window and the phone screen.
/// </summary>
public sealed class FormatRing
{
    public const string ZipAllId = "__zip_all__";
    public const string MoreId = "__more__";

    /// <summary>Overall popularity, used to order the formats behind "More".</summary>
    private static readonly string[] Popular =
        ["mp4", "mp3", "mov", "gif", "jpg", "png", "pdf", "docx", "webp", "m4a", "wav", "webm", "mkv", "md", "txt",
         "html", "csv", "json", "xlsx", "srt", "zip", "folder"];

    private readonly List<DonutItem> _featured;
    private readonly List<DonutItem> _rest;
    private readonly int _slicesPerPage;

    private FormatRing(List<DonutItem> featured, List<DonutItem> rest, bool hasUnknown, int slicesPerPage)
    {
        _featured = featured;
        _rest = rest;
        _slicesPerPage = slicesPerPage;
        HasUnknownFiles = hasUnknown;
    }

    /// <summary>Some files aren't a type Bakkwo knows; they can still be zipped.</summary>
    public bool HasUnknownFiles { get; }

    public int Page { get; private set; }

    public int PageCount => _rest.Count == 0 ? 1 : 1 + (int)Math.Ceiling(_rest.Count / (double)_slicesPerPage);

    /// <summary>True when the files have no format to convert to at all (only "ZIP" is possible).</summary>
    public bool OnlyZip => _featured.Concat(_rest).All(i => i.Id is ZipAllId or "zip");

    /// <param name="maxSuggestions">How many suggestions the first page shows.</param>
    /// <param name="slicesPerPage">How many formats each "More" page shows.</param>
    /// <param name="exclude">Target format ids not to offer (the phone app leaves out "folder").</param>
    public static FormatRing For(IReadOnlyList<string> files, ConversionEngine engine, int maxSuggestions = 7, int slicesPerPage = 11,
        IReadOnlySet<string>? exclude = null)
    {
        var formats = files.Select(FormatCatalog.Detect).ToList();
        var featured = new List<DonutItem>();
        var rest = new List<DonutItem>();

        if (formats.All(f => f is not null))
        {
            var sources = formats.Select(f => f!).ToList();
            var targets = engine.Planner.CommonTargets(sources).Where(t => exclude?.Contains(t.Id) != true).ToList();

            // Only offer subtitle formats when every video has a text subtitle track (picture ones can't become SRT).
            if (targets.Any(t => t.Category == FormatCategory.Subtitle)
                && sources.Any(f => f.Category == FormatCategory.Video)
                && engine.Registry.Converters.OfType<FfmpegConverter>().FirstOrDefault() is { } ffmpeg
                && !files.Take(5).All(ffmpeg.HasTextSubtitles))
            {
                targets.RemoveAll(t => t.Category == FormatCategory.Subtitle);
            }

            var single = sources.Select(f => f.Id).Distinct().Count() == 1 ? sources[0].Id : null;

            // First page: suggestions for this kind of file, in a fixed, predictable order.
            foreach (var id in Suggested(sources))
            {
                if (featured.Count >= maxSuggestions) break;
                if (id == single) continue;
                if (targets.FirstOrDefault(t => t.Id == id) is { } target) featured.Add(Item(target, single));
            }
            // "Make it smaller" (same format) is a top choice for photos and videos.
            if (single is not null && targets.FirstOrDefault(t => t.Id == single) is { } self)
            {
                featured.Insert(Math.Min(2, featured.Count), Item(self, single));
                if (featured.Count > maxSuggestions) featured.RemoveAt(featured.Count - 1);
            }

            // Everything else, behind "More", popular formats first.
            rest.AddRange(targets
                .Where(t => featured.All(f => f.Id != t.Id))
                .Select((t, i) => (t, i))
                .OrderBy(x => Array.IndexOf(Popular, x.t.Id) is var p and >= 0 ? p : 100 + x.i)
                .Select(x => Item(x.t, single)));
        }
        if (featured.Concat(rest).All(i => i.Id != "zip"))
        {
            rest.Add(new DonutItem(ZipAllId, "ZIP", Strings.CaptionArchive, files.Count > 1 ? Strings.ZipAllHint : Strings.ZipHint));
        }
        if (featured.Count == 0)
        {
            // Nothing known to suggest (unknown files): show what there is.
            featured.AddRange(rest.Take(maxSuggestions));
            rest.RemoveRange(0, featured.Count);
        }

        return new FormatRing(featured, rest, formats.Any(f => f is null), slicesPerPage);
    }

    /// <summary>The slices for the current page, with a "More"/"Back" slice when there are several pages.</summary>
    public IReadOnlyList<DonutItem> CurrentItems()
    {
        var items = Page == 0
            ? new List<DonutItem>(_featured)
            : _rest.Skip((Page - 1) * _slicesPerPage).Take(_slicesPerPage).ToList();

        if (PageCount > 1)
        {
            var last = Page == PageCount - 1;
            items.Add(new DonutItem(MoreId,
                last ? Strings.BackLabel : Strings.MoreLabel,
                $"{Page + 1}/{PageCount}",
                last ? Strings.BackHint : Strings.MoreHint(_rest.Count),
                Special: true));
        }
        return items;
    }

    public void NextPage() => Page = (Page + 1) % PageCount;

    /// <summary>What most people want for each kind of file, best first.</summary>
    private static IEnumerable<string> Suggested(IReadOnlyList<FileFormat> sources) =>
        sources.Select(f => f.Category).Distinct().ToList() switch
        {
            // "srt" only exists when the video has text subtitles; then it's one of the most useful picks.
            [FormatCategory.Video] => ["mp4", "mov", "mp3", "srt", "gif", "m4a", "webm", "mkv"],
            [FormatCategory.Audio] => ["mp3", "m4a", "wav", "flac", "opus", "aac", "ogg"],
            [FormatCategory.Image] => ["jpg", "png", "webp", "pdf", "ico", "gif"],
            [FormatCategory.Document] => ["pdf", "docx", "md", "txt", "html", "odt"],
            [FormatCategory.Text] => ["html", "pdf", "docx", "txt", "md"],
            [FormatCategory.Data] => ["json", "csv", "yaml", "xml", "tsv"],
            [FormatCategory.Subtitle] => ["srt", "vtt", "ass"],
            [FormatCategory.Archive] => ["folder", "zip", "tgz", "tar"],
            _ => Popular,
        };

    /// <summary>A ring slice: format name, a small caption ("audio", "smaller"), and the hint shown in the middle.</summary>
    private static DonutItem Item(FileFormat target, string? sameAs)
    {
        if (target.IsFolder) return new DonutItem(target.Id, Strings.Extract, Strings.CaptionFolder, Strings.ExtractHint);
        if (target.Id == sameAs) return new DonutItem(target.Id, target.Label, Strings.Smaller, Strings.SmallerHint(target.Label));
        return new DonutItem(target.Id, target.Label, Strings.Caption(target.Category), Strings.ConvertHint(target));
    }
}
