using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SixLabors.ImageSharp;
using Color = DocumentFormat.OpenXml.Wordprocessing.Color;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using WTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

namespace Bakkwo.Core.Word;

/// <summary>
/// Writes a .docx from Markdown (via Markdig's syntax tree) or from plain text, using real Word
/// styles (Heading 1–6, Title, Quote, code), numbered and bulleted lists, tables, links and pictures.
/// </summary>
public sealed partial class DocxWriter
{
    private const long MaxImageWidthEmu = 5_670_000;   // ~15.75 cm, fits A4 with 2.54 cm margins
    private const int BulletAbstractId = 1;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly MainDocumentPart _main;
    private readonly Body _body;
    private readonly string? _baseDirectory;
    private readonly List<NumberingInstance> _numberingInstances = [];
    private readonly List<AbstractNum> _orderedLists = [];
    private uint _drawingId;

    private sealed record RunFormat(bool Bold = false, bool Italic = false, bool Strike = false, bool Code = false, string? CharStyle = null);

    private DocxWriter(MainDocumentPart main, string? baseDirectory)
    {
        _main = main;
        _baseDirectory = baseDirectory;
        _body = new Body();
        main.Document = new Document(_body);
        AddStyles(main);
    }

    public static void FromMarkdown(string markdown, string outputPath, string? baseDirectory = null, string? title = null)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var writer = new DocxWriter(doc.AddMainDocumentPart(), baseDirectory);
        var parsed = Markdown.Parse(markdown, Pipeline);
        foreach (var block in parsed) writer.Block(block, listLevel: 0);
        writer.Finish();
        SetProperties(doc, title ?? parsed.OfType<HeadingBlock>().FirstOrDefault()?.Inline?.FirstChild?.ToString());
    }

    public static void FromPlainText(string text, string outputPath, string? title = null)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var writer = new DocxWriter(doc.AddMainDocumentPart(), null);
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        foreach (var block in BlankLines().Split(normalized))
        {
            var paragraph = new Paragraph();
            var lines = block.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) paragraph.AppendChild(new Run(new Break()));
                paragraph.AppendChild(writer.TextRun(lines[i], new RunFormat()));
            }
            writer._body.AppendChild(paragraph);
        }
        writer.Finish();
        SetProperties(doc, title);
    }

    private static void SetProperties(WordprocessingDocument doc, string? title)
    {
        doc.PackageProperties.Creator = "Bakkwo";
        if (!string.IsNullOrWhiteSpace(title)) doc.PackageProperties.Title = title;
    }

    private void Finish()
    {
        if (_numberingInstances.Count > 0)
        {
            var numbering = new Numbering();
            numbering.Append(AbstractList(BulletAbstractId, bullet: true));
            foreach (var ordered in _orderedLists) numbering.Append(ordered);
            foreach (var instance in _numberingInstances) numbering.Append(instance);
            _main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;
        }

        // A4 portrait, 2.54 cm margins.
        _body.AppendChild(new SectionProperties(
            new PageSize { Width = 11906U, Height = 16838U },
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440U, Right = 1440U, Header = 708U, Footer = 708U, Gutter = 0U }));
    }

    // ------------------------------------------------------------ blocks

    private void Block(Block block, int listLevel)
    {
        switch (block)
        {
            case HeadingBlock heading:
                _body.AppendChild(StyledParagraph(heading.Level == 1 && IsFirstBlock(heading) ? "Title" : $"Heading{heading.Level}", heading.Inline));
                break;
            case ParagraphBlock paragraph:
                _body.AppendChild(StyledParagraph(null, paragraph.Inline));
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is ParagraphBlock qp) _body.AppendChild(StyledParagraph("Quote", qp.Inline));
                    else Block(child, listLevel);
                }
                break;
            case ListBlock list:
                ListBlockToWord(list, listLevel);
                break;
            case FencedCodeBlock or CodeBlock:
                var code = (LeafBlock)block;
                foreach (var line in code.Lines.Lines.Take(code.Lines.Count))
                {
                    var p = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Code" }));
                    p.AppendChild(TextRun(line.Slice.ToString(), new RunFormat()));
                    _body.AppendChild(p);
                }
                break;
            case ThematicBreakBlock:
                _body.AppendChild(new Paragraph(new ParagraphProperties(new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 1U, Color = "BFBFBF" }))));
                break;
            case MdTable table:
                _body.AppendChild(TableToWord(table));
                _body.AppendChild(new Paragraph());
                break;
            case HtmlBlock html:
                var text = Regex.Replace(html.Lines.ToString(), "<[^>]+>", "").Trim();
                if (text.Length > 0) _body.AppendChild(new Paragraph(TextRun(System.Net.WebUtility.HtmlDecode(text), new RunFormat())));
                break;
            case ContainerBlock container:
                foreach (var child in container) Block(child, listLevel);
                break;
        }
    }

    private static bool IsFirstBlock(Block block) => block.Parent is MarkdownDocument doc && doc.IndexOf(block) == 0;

    private Paragraph StyledParagraph(string? style, ContainerInline? inline)
    {
        var paragraph = new Paragraph();
        if (style is not null) paragraph.AppendChild(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        if (inline is not null) Inlines(inline, paragraph, new RunFormat(), link: null);
        return paragraph;
    }

    private void ListBlockToWord(ListBlock list, int level)
    {
        var numId = NewNumbering(list.IsOrdered, list.IsOrdered && int.TryParse(list.OrderedStart, out var s) ? s : 1);
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var first = true;
            foreach (var child in item)
            {
                if (child is ListBlock nested)
                {
                    ListBlockToWord(nested, Math.Min(level + 1, 8));
                    continue;
                }
                if (child is ParagraphBlock pb)
                {
                    var props = new ParagraphProperties(new ParagraphStyleId { Val = "ListParagraph" });
                    if (first)
                    {
                        props.AppendChild(new NumberingProperties(
                            new NumberingLevelReference { Val = level },
                            new NumberingId { Val = numId }));
                    }
                    else
                    {
                        props.AppendChild(new Indentation { Left = (720 * (level + 1)).ToString() });
                    }
                    var paragraph = new Paragraph(props);
                    if (pb.Inline is not null) Inlines(pb.Inline, paragraph, new RunFormat(), link: null);
                    _body.AppendChild(paragraph);
                    first = false;
                }
                else
                {
                    Block(child, level);
                }
            }
        }
    }

    private int NewNumbering(bool ordered, int start)
    {
        var id = _numberingInstances.Count + 1;
        var abstractId = BulletAbstractId;
        if (ordered)
        {
            // One list definition per numbered list, so each starts at its own number. Word,
            // LibreOffice and Google Docs all keep this through DOC/ODT/PDF conversions.
            abstractId = 100 + _orderedLists.Count;
            _orderedLists.Add(AbstractList(abstractId, bullet: false, start));
        }
        _numberingInstances.Add(new NumberingInstance(new AbstractNumId { Val = abstractId }) { NumberID = id });
        return id;
    }

    private WTable TableToWord(MdTable table)
    {
        var columns = table.OfType<MdTableRow>().Select(r => r.Count).DefaultIfEmpty(1).Max();
        var word = new WTable(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));
        var grid = new TableGrid();
        for (var i = 0; i < columns; i++) grid.AppendChild(new GridColumn { Width = (9026 / columns).ToString() });
        word.AppendChild(grid);

        foreach (var row in table.OfType<MdTableRow>())
        {
            var wordRow = new WTableRow();
            if (row.IsHeader) wordRow.AppendChild(new TableRowProperties(new TableHeader()));
            foreach (var cell in row.OfType<MdTableCell>())
            {
                var wordCell = new WTableCell();
                var paragraphs = cell.OfType<ParagraphBlock>().ToList();
                if (paragraphs.Count == 0) wordCell.AppendChild(new Paragraph());
                foreach (var pb in paragraphs)
                {
                    var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }));
                    if (pb.Inline is not null) Inlines(pb.Inline, paragraph, new RunFormat(Bold: row.IsHeader), link: null);
                    wordCell.AppendChild(paragraph);
                }
                wordRow.AppendChild(wordCell);
            }
            for (var i = row.Count; i < columns; i++) wordRow.AppendChild(new WTableCell(new Paragraph()));
            word.AppendChild(wordRow);
        }
        return word;
    }

    // ------------------------------------------------------------ inlines

    private void Inlines(ContainerInline container, OpenXmlElement target, RunFormat format, string? link)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendRun(target, TextRun(literal.Content.ToString(), format), link);
                    break;
                case EmphasisInline emphasis:
                    var f = emphasis.DelimiterChar == '~'
                        ? format with { Strike = true }
                        : emphasis.DelimiterCount >= 2 ? format with { Bold = true } : format with { Italic = true };
                    if (emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount >= 3) f = f with { Italic = true };
                    Inlines(emphasis, target, f, link);
                    break;
                case CodeInline code:
                    AppendRun(target, TextRun(code.Content, format with { Code = true }), link);
                    break;
                case LinkInline { IsImage: true } image:
                    AppendRun(target, ImageRun(image.Url, image.FirstChild?.ToString() ?? image.Title ?? "image")
                        ?? TextRun(image.FirstChild?.ToString() ?? "", format), link: null);
                    break;
                case LinkInline linkInline:
                    var url = linkInline.GetDynamicUrl?.Invoke() ?? linkInline.Url;
                    if (TryAbsolute(url, out _))
                    {
                        var hyperlink = NewHyperlink(url!);
                        target.AppendChild(hyperlink);
                        Inlines(linkInline, hyperlink, format with { CharStyle = "Hyperlink" }, url);
                    }
                    else Inlines(linkInline, target, format, link);
                    break;
                case AutolinkInline auto:
                    var autoUrl = auto.IsEmail ? "mailto:" + auto.Url : auto.Url;
                    if (TryAbsolute(autoUrl, out _))
                    {
                        var hyperlink = NewHyperlink(autoUrl);
                        hyperlink.AppendChild(TextRun(auto.Url, format with { CharStyle = "Hyperlink" }));
                        target.AppendChild(hyperlink);
                    }
                    else AppendRun(target, TextRun(auto.Url, format), link);
                    break;
                case LineBreakInline lineBreak:
                    AppendRun(target, lineBreak.IsHard ? new Run(new Break()) : TextRun(" ", format), link);
                    break;
                case HtmlEntityInline entity:
                    AppendRun(target, TextRun(entity.Transcoded.ToString(), format), link);
                    break;
                case HtmlInline html when BreakTag().IsMatch(html.Tag):
                    AppendRun(target, new Run(new Break()), link);
                    break;
                case TaskList task:
                    AppendRun(target, TextRun(task.Checked ? "☑ " : "☐ ", format), link);
                    break;
                case ContainerInline nested:
                    Inlines(nested, target, format, link);
                    break;
            }
        }
    }

    private static void AppendRun(OpenXmlElement target, Run run, string? link) => target.AppendChild(run);

    private Hyperlink NewHyperlink(string url)
    {
        var relationship = _main.AddHyperlinkRelationship(new Uri(url, UriKind.Absolute), isExternal: true);
        return new Hyperlink { Id = relationship.Id, History = true };
    }

    private static bool TryAbsolute(string? url, out Uri? uri)
    {
        uri = null;
        return url is not null && Uri.TryCreate(url, UriKind.Absolute, out uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }

    private Run TextRun(string text, RunFormat format)
    {
        var run = new Run();
        var props = new RunProperties();
        if (format.CharStyle is not null) props.AppendChild(new RunStyle { Val = format.CharStyle });
        else if (format.Code) props.AppendChild(new RunStyle { Val = "CodeChar" });
        if (format.Bold) props.AppendChild(new Bold());
        if (format.Italic) props.AppendChild(new Italic());
        if (format.Strike) props.AppendChild(new Strike());
        if (props.HasChildren) run.AppendChild(props);

        // Tabs must be <w:tab/>, not a tab character inside <w:t>.
        var parts = text.Split('\t');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) run.AppendChild(new TabChar());
            if (parts[i].Length > 0) run.AppendChild(new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    // ------------------------------------------------------------ pictures

    private Run? ImageRun(string? url, string alt)
    {
        var bytes = LoadImageBytes(url);
        if (bytes is null) return null;

        int width, height;
        byte[] png;
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(bytes);
            (width, height) = (image.Width, image.Height);
            using var buffer = new MemoryStream();
            image.SaveAsPng(buffer); // Word doesn't read WebP/TGA, so pictures are stored as PNG.
            png = buffer.ToArray();
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return null;
        }

        var part = _main.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(png)) part.FeedData(stream);
        var relId = _main.GetIdOfPart(part);

        long cx = width * 9525L, cy = height * 9525L; // 96 dpi pixels → EMU
        if (cx > MaxImageWidthEmu)
        {
            cy = cy * MaxImageWidthEmu / cx;
            cx = MaxImageWidthEmu;
        }

        var id = ++_drawingId;
        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = $"Picture {id}", Description = alt },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = $"image{id}.png" },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U,
            });
        return new Run(drawing);
    }

    private byte[]? LoadImageBytes(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = url.IndexOf(',');
            if (comma < 0 || !url[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase)) return null;
            try { return Convert.FromBase64String(url[(comma + 1)..]); }
            catch (FormatException) { return null; }
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https") return null; // offline only

        var path = Uri.UnescapeDataString(url);
        if (!Path.IsPathRooted(path) && _baseDirectory is not null) path = Path.Combine(_baseDirectory, path);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    // ------------------------------------------------------------ styles and numbering

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    // East Asian font so Korean text uses a Korean face in Word on Windows and Mac.
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Malgun Gothic", ComplexScript = "Arial" },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" },
                    new Languages { Val = "en-US", EastAsia = "ko-KR" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "160", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));

        styles.Append(new Style(new StyleName { Val = "Normal" }, new PrimaryStyle())
            { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });

        styles.Append(ParagraphStyle("Title", "Title", 56, bold: true, outline: 0, color: "1F1A4D", before: 0, after: 240));
        int[] sizes = [36, 30, 26, 24, 22, 22];
        for (var level = 1; level <= 6; level++)
        {
            styles.Append(ParagraphStyle($"Heading{level}", $"heading {level}", sizes[level - 1], bold: true,
                outline: level - 1, color: level <= 2 ? "3B2FA8" : "262626", before: level <= 2 ? 360 : 240, after: 120,
                italic: level == 6));
        }

        styles.Append(new Style(
            new StyleName { Val = "Quote" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new PrimaryStyle(),
            new StyleParagraphProperties(
                new ParagraphBorders(new LeftBorder { Val = BorderValues.Single, Size = 18U, Space = 8U, Color = "8A78FF" }),
                new Indentation { Left = "360" }),
            new StyleRunProperties(new Italic(), new Color { Val = "555555" }))
            { Type = StyleValues.Paragraph, StyleId = "Quote" });

        styles.Append(new Style(
            new StyleName { Val = "List Paragraph" }, new BasedOn { Val = "Normal" }, new PrimaryStyle(),
            new StyleParagraphProperties(new SpacingBetweenLines { After = "80" }, new ContextualSpacing()))
            { Type = StyleValues.Paragraph, StyleId = "ListParagraph" });

        var mono = new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", EastAsia = "Malgun Gothic", ComplexScript = "Consolas" };
        styles.Append(new Style(
            new StyleName { Val = "Code" }, new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F3F3F7" },
                new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
            new StyleRunProperties((RunFonts)mono.CloneNode(true), new FontSize { Val = "19" }))
            { Type = StyleValues.Paragraph, StyleId = "Code" });

        styles.Append(new Style(
            new StyleName { Val = "Code Char" },
            new StyleRunProperties((RunFonts)mono.CloneNode(true), new FontSize { Val = "20" },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F3F3F7" }))
            { Type = StyleValues.Character, StyleId = "CodeChar" });

        styles.Append(new Style(
            new StyleName { Val = "Hyperlink" },
            new StyleRunProperties(new Color { Val = "0563C1" }, new Underline { Val = UnderlineValues.Single }))
            { Type = StyleValues.Character, StyleId = "Hyperlink" });

        styles.Append(new Style(
            new StyleName { Val = "Table Grid" }, new BasedOn { Val = "TableNormal" },
            new StyleParagraphProperties(new SpacingBetweenLines { After = "0" }),
            new StyleTableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4U, Color = "BFBFBF" },
                new LeftBorder { Val = BorderValues.Single, Size = 4U, Color = "BFBFBF" },
                new BottomBorder { Val = BorderValues.Single, Size = 4U, Color = "BFBFBF" },
                new RightBorder { Val = BorderValues.Single, Size = 4U, Color = "BFBFBF" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Color = "BFBFBF" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Color = "BFBFBF" }),
                new TableCellMarginDefault(
                    new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa })))
            { Type = StyleValues.Table, StyleId = "TableGrid" });

        styles.Append(new Style(new StyleName { Val = "Normal Table" }, new UIPriority { Val = 99 }, new SemiHidden(),
            new StyleTableProperties(new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa }))
            { Type = StyleValues.Table, StyleId = "TableNormal", Default = true });

        main.AddNewPart<StyleDefinitionsPart>().Styles = styles;
    }

    private static Style ParagraphStyle(string id, string name, int halfPoints, bool bold, int outline, string color,
        int before, int after, bool italic = false)
    {
        var run = new StyleRunProperties();
        if (bold) run.AppendChild(new Bold());
        if (italic) run.AppendChild(new Italic());
        run.AppendChild(new Color { Val = color });
        run.AppendChild(new FontSize { Val = halfPoints.ToString() });
        run.AppendChild(new FontSizeComplexScript { Val = halfPoints.ToString() });

        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new KeepNext(),
                new KeepLines(),
                new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() },
                new OutlineLevel { Val = outline }),
            run)
        { Type = StyleValues.Paragraph, StyleId = id };
    }

    private static AbstractNum AbstractList(int id, bool bullet, int start = 1)
    {
        string[] bullets = ["•", "◦", "▪"];
        var abstractNum = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }) { AbstractNumberId = id };
        for (var level = 0; level < 9; level++)
        {
            var lvl = new Level(
                new StartNumberingValue { Val = level == 0 ? start : 1 },
                new NumberingFormat
                {
                    Val = bullet ? NumberFormatValues.Bullet
                        : (level % 3) switch { 0 => NumberFormatValues.Decimal, 1 => NumberFormatValues.LowerLetter, _ => NumberFormatValues.LowerRoman },
                },
                new LevelText { Val = bullet ? bullets[level % 3] : $"%{level + 1}." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = (720 * (level + 1)).ToString(), Hanging = "360" }))
            { LevelIndex = level };
            if (bullet)
            {
                lvl.AppendChild(new NumberingSymbolRunProperties(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", Hint = FontTypeHintValues.Default }));
            }
            abstractNum.AppendChild(lvl);
        }
        return abstractNum;
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^<br\s*/?>$", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();
}
