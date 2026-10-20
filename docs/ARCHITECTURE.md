# Architecture

## The format graph

Every format in `FormatCatalog` is a node. Every converter exposes `Edges` — pairs like
`yaml → json` — each with a cost (default 1). `RoutePlanner` runs Dijkstra over the edges of the
*available* converters, so if FFmpeg is missing its edges never enter the graph.

Ties on cost are broken by fewer steps, and chains are capped at `MaxSteps` (3), because every hop
costs time and can lose information.

```
yaml ─┐            ┌─► yaml
csv  ─┼─► json ────┼─► csv          md ─► html ─► txt
tsv  ─┤            ├─► tsv          txt ─► html
xml  ─┘            └─► xml
```

`DataConverter` only implements *spoke ↔ hub* (8 edges) yet supports all 20 pairs between five
formats; the planner fills in the rest.

## Running a conversion

`ConversionEngine.ConvertAsync`:

1. Detects the source format from the file name.
2. Asks the planner for a route; fails early with a readable message if there is none.
3. Reserves the output path (`OutputPaths`) by creating an empty placeholder under a lock, so
   parallel jobs that would produce the same name get `name (1).ext` instead of racing.
4. Runs each hop. Intermediate files go to a private temp folder; only the last hop writes the
   real output. Progress from each hop is scaled into the overall 0–1 range.
5. On failure or cancellation, deletes the placeholder and the temp folder.

`ConvertManyAsync` runs jobs in parallel (CPU count − 1, or 2 for FFmpeg jobs, which are already
multi-threaded) and reports per-file progress.

## The ring (`DonutMenu`)

A plain Avalonia `Control` that draws itself in `Render`. Each slice is a `StreamGeometry`:
outer arc clockwise, line inward, inner arc counter-clockwise. Hit-testing converts the pointer
to polar coordinates: the radius decides ring vs. centre, and `atan2` (shifted so 0° is 12 o'clock)
picks the slice.

`MainWindow` shows the ring during `DragEnter` if the platform exposes the file list mid-drag,
highlights slices on `DragOver`, and converts on `Drop`. If the drop lands in the middle or outside,
the ring stays pinned and waits for a click.

## Adding a converter

```csharp
public sealed class MyConverter : ConverterBase
{
    public override string Name => "My formats";
    public override IEnumerable<ConversionEdge> Edges => [Edge("abc", "json")];

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options,
        IProgress<double>? progress, CancellationToken ct)
    {
        // read step.InputPath, write step.OutputPath
    }
}
```

Add the format to `FormatCatalog` and the converter to `ConverterRegistry.CreateDefault()`.
The ring, the CLI and multi-step routes (`abc → json → csv`) work immediately.

## Word documents

`WordConverter` handles DOCX/DOCM/DOTX in-process with the Open XML SDK:

- `DocxReader` walks the body (paragraphs, tables, content controls) and writes Markdown or text.
  Headings are found by style *name* ("heading 1", "title") along the `basedOn` chain, because
  Korean and other localized Word versions use different style ids. List numbering is counted per
  abstract list definition, the way Word numbers, so lists split across instances keep counting.
- `DocxWriter` builds a document from Markdig's syntax tree with real Word styles, one list
  definition per numbered list (so each restarts at its own number in Word, LibreOffice and Google
  Docs), tables, hyperlinks and pictures stored as PNG.

`LibreOfficeConverter` adds DOC, DOT, RTF, ODT and exact-layout PDF by running `soffice --headless`
with a private profile per concurrent job. Its edges cost 2, so the planner prefers the built-in
route when both exist (DOCX → Markdown) and uses LibreOffice only where it's needed (DOCX → PDF,
DOC → DOCX → Markdown).

## Audio, video and subtitles

`FfmpegConverter` runs `ffmpeg -encoders` once and only offers targets that build can write,
choosing fallbacks where possible (OGG uses Vorbis, else Opus). Video → GIF edges are *terminal*: the
planner never chains from them, so a video can't wander into still-image formats via GIF. Converting
a format to itself (a self edge) re-encodes it to shrink or compress, and the result is named
`name (720p).ext` / `name (compressed).ext`. Subtitle targets are offered in the ring only when
`ffprobe` finds a subtitle track. `.ts` files must show MPEG-TS packets (0x47 every 188 or 192 bytes
within the first 64 KB), so TypeScript sources aren't mistaken for video while HLS segments with an
ID3 header still are.

When converting video → video, `PlanSubtitles` decides per subtitle track: text tracks are re-encoded
for the target (mov_text, WebVTT, SRT/ASS), picture tracks are copied where the container allows
(MKV, DVB in TS), and otherwise the first picture track is burned in with an `overlay` filter.
`tests/Pgs.cs` writes a tiny PGS file so this path is tested with real DVB subtitles.

## Desktop and phone

The engine (`Bakkwo.Core`) has no UI and no platform code, so the same converters run on a computer and
on a phone. What differs is which converters are registered:

| | Desktop (`Bakkwo.App`) | Phone (`Bakkwo.Mobile` + `Bakkwo.Android`) |
| --- | --- | --- |
| Built in | Image, ImageToPdf, Data, Text, Word, Archive | the same |
| External programs | FFmpeg, LibreOffice (when installed) | none: apps can't start other programs |
| Phone's own decoders | — | `HeifImageConverter` (HEIC/AVIF → JPG/PNG/WebP), `AudioExtractConverter` (video → M4A) |

Because routes are planned over whatever edges exist, adding `heic → jpg` on Android is enough for the ring
to offer HEIC → PDF, GIF, ICO and the rest (through JPG/PNG and the normal image converters).

`Bakkwo.UI` holds what both screens share: the `DonutMenu` control, `FormatRing` (the suggestions, "More"
pages and the ZIP fallback for a set of files), `Strings` and `Settings`. The phone ring is the same
control; instead of dragging with a mouse you touch a slice, or slide your finger around the ring and let
go on one. Results go to the app's cache and leave it through **Save** (MediaStore → Download/Bakkwo on
Android 10+, a save dialog before that) or **Share** (a `FileProvider` link to the share sheet).

`Bakkwo.Mobile` talks to Android only through `IPhonePlatform`, so an iPhone app needs a new head
project with its own share/save code and decoders (Image I/O, AVFoundation); the screen stays the same.
