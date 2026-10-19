using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace Bakkwo.Core.Word;

/// <summary>
/// Reads .docx / .docm / .dotx and writes Markdown or plain text: headings, bullet and numbered
/// lists, tables, bold/italic/strikethrough, links and pictures (embedded as data URIs).
/// </summary>
public sealed partial class DocxReader
{
    private enum Output { Markdown, Text }

    private readonly MainDocumentPart _main;
    private readonly Output _output;
    private readonly Dictionary<string, StyleInfo> _styles = new();
    private readonly Dictionary<(int AbstractId, int Level), int> _counters = new();
    private readonly HashSet<int> _startedInstances = new();

    private sealed record StyleInfo(string Name, int? OutlineLevel, string? BasedOn, NumberingProperties? Numbering);

    private sealed record Segment(string Text, bool Bold, bool Italic, bool Strike, bool Code, string? Link, bool IsImage = false);

    private DocxReader(MainDocumentPart main, Output output)
    {
        _main = main;
        _output = output;
        foreach (var style in main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is not { } id) continue;
            _styles[id] = new StyleInfo(
                style.StyleName?.Val?.Value?.ToLowerInvariant() ?? id.ToLowerInvariant(),
                style.StyleParagraphProperties?.OutlineLevel?.Val?.Value,
                style.BasedOn?.Val?.Value,
                style.StyleParagraphProperties?.NumberingProperties);
        }
    }

    public static string ToMarkdown(string path) => Read(path, Output.Markdown);

    public static string ToPlainText(string path) => Read(path, Output.Text);

    private static string Read(string path, Output output)
    {
        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("This Word file has no document body.");
        var body = main.Document?.Body ?? throw new InvalidDataException("This Word file has no document body.");
        return new DocxReader(main, output).Body(body);
    }

    // ------------------------------------------------------------ blocks

    private enum Kind { None, Paragraph, ListItem, Code }

    private sealed record BlockText(string Text, Kind Kind, int ListId = 0);

    private string Body(OpenXmlElement container)
    {
        var sb = new StringBuilder();
        BlockText? previous = null;
        var inCode = false;

        void CloseCode()
        {
            if (!inCode) return;
            if (_output == Output.Markdown) sb.Append("\n```");
            inCode = false;
        }

        foreach (var block in Blocks(container))
        {
            switch (block)
            {
                case Paragraph p:
                    var current = ParagraphText(p);
                    if (current.Kind == Kind.None) continue;

                    if (current.Kind == Kind.Code)
                    {
                        if (inCode) sb.Append('\n');
                        else
                        {
                            if (sb.Length > 0) sb.Append("\n\n");
                            if (_output == Output.Markdown) sb.Append("```\n");
                            inCode = true;
                        }
                        sb.Append(current.Text);
                        previous = current;
                        continue;
                    }

                    CloseCode();
                    if (sb.Length > 0)
                    {
                        // Items of the same list stay together; a different list starts after a blank line.
                        var sameList = current.Kind == Kind.ListItem && previous?.Kind == Kind.ListItem
                            && (previous.ListId == current.ListId || current.Text.StartsWith(' '));
                        sb.Append(sameList ? "\n" : "\n\n");
                    }
                    sb.Append(current.Text);
                    previous = current;
                    break;
                case Table t:
                    var table = TableText(t);
                    if (table.Length == 0) continue;
                    CloseCode();
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append(table);
                    previous = new BlockText(table, Kind.Paragraph);
                    break;
            }
        }
        CloseCode();
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Paragraphs and tables in reading order, looking inside content controls.</summary>
    private static IEnumerable<OpenXmlElement> Blocks(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph or Table:
                    yield return child;
                    break;
                case SdtBlock sdt when sdt.SdtContentBlock is { } content:
                    foreach (var inner in Blocks(content)) yield return inner;
                    break;
                case CustomXmlBlock custom:
                    foreach (var inner in Blocks(custom)) yield return inner;
                    break;
            }
        }
    }

    private BlockText ParagraphText(Paragraph p)
    {
        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var styleName = StyleName(styleId);

        // Code paragraphs: raw text, kept as one fenced block in Markdown.
        if (styleName is "code" or "html preformatted" or "plain text" or "source code")
        {
            var raw = string.Concat(p.Descendants<Text>().Select(t => t.Text));
            return new BlockText(raw, Kind.Code);
        }

        var inline = Inline(p);
        if (inline.Trim().Length == 0) return new BlockText("", Kind.None);

        var heading = HeadingLevel(p, styleId);
        if (heading is int level)
        {
            return _output == Output.Markdown
                ? new BlockText(new string('#', level) + " " + inline.Trim(), Kind.Paragraph)
                : new BlockText(inline.Trim(), Kind.Paragraph);
        }

        if (ListInfo(p, styleId) is { } list)
        {
            var indent = new string(' ', list.Level * 3);
            string marker;
            if (list.Bullet) marker = _output == Output.Markdown ? "- " : "• ";
            else
            {
                // Word numbers by the underlying abstract list: separate instances continue each other
                // unless an instance restarts with a start override.
                var key = (list.AbstractId, list.Level);
                if (list.StartOverride is int start && _startedInstances.Add(list.NumId * 16 + list.Level))
                    _counters[key] = start - 1;
                var n = _counters.GetValueOrDefault(key) + 1;
                _counters[key] = n;
                // Deeper levels restart when a shallower item appears.
                foreach (var deeper in _counters.Keys.Where(k => k.AbstractId == list.AbstractId && k.Level > list.Level).ToList())
                    _counters.Remove(deeper);
                marker = $"{n}. ";
            }
            return new BlockText(indent + marker + inline.Trim(), Kind.ListItem, list.AbstractId);
        }

        if (_output == Output.Markdown && styleName is "quote" or "intense quote")
        {
            return new BlockText("> " + inline.Trim().Replace("\n", "\n> "), Kind.Paragraph);
        }
        var plain = inline.TrimEnd();
        // A normal paragraph that starts like "- x" or "1. x" must not turn into a list in Markdown.
        if (_output == Output.Markdown && LooksLikeListMarker().IsMatch(plain)) plain = "\\" + plain.TrimStart();
        return new BlockText(plain, Kind.Paragraph);
    }

    private int? HeadingLevel(Paragraph p, string? styleId)
    {
        var outline = p.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (outline is int o && o < 6) return o + 1;

        // Walk the style chain: Korean Word uses ids like "1" but names stay "heading 1".
        for (var id = styleId; id is not null && _styles.TryGetValue(id, out var style); id = style.BasedOn)
        {
            if (style.Name == "title") return 1;
            if (style.Name == "subtitle") return 2;
            if (style.Name.StartsWith("heading ", StringComparison.Ordinal)
                && int.TryParse(style.Name.AsSpan(8), out var n) && n is >= 1 and <= 6) return n;
            if (style.OutlineLevel is int so && so < 6) return so + 1;
            if (id == style.BasedOn) break;
        }
        return null;
    }

    private string? StyleName(string? styleId) =>
        styleId is not null && _styles.TryGetValue(styleId, out var s) ? s.Name : null;

    private sealed record ListLevel(int NumId, int AbstractId, int Level, bool Bullet, int? StartOverride);

    private ListLevel? ListInfo(Paragraph p, string? styleId)
    {
        var numPr = p.ParagraphProperties?.NumberingProperties;
        if (numPr?.NumberingId is null && styleId is not null && _styles.TryGetValue(styleId, out var style))
        {
            numPr = style.Numbering;
        }
        var numId = numPr?.NumberingId?.Val?.Value;
        if (numId is null or 0) return null;
        var level = numPr?.NumberingLevelReference?.Val?.Value ?? 0;

        var numbering = _main.NumberingDefinitionsPart?.Numbering;
        var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        var abstractId = instance?.AbstractNumId?.Val?.Value ?? -numId.Value;
        var startOverride = instance?.Elements<LevelOverride>()
            .FirstOrDefault(o => o.LevelIndex?.Value == level)?.StartOverrideNumberingValue?.Val?.Value;

        var abstractNum = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
        var lvl = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        var format = lvl?.NumberingFormat?.Val?.Value;
        var bullet = format is null || format == NumberFormatValues.Bullet || format == NumberFormatValues.None;

        return new ListLevel(numId.Value, abstractId, level, bullet, startOverride);
    }

    // ------------------------------------------------------------ tables

    private string TableText(Table table)
    {
        var rows = new List<List<string>>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var paragraphs = cell.Descendants<Paragraph>().Select(p => Inline(p).Trim()).Where(t => t.Length > 0);
                var text = string.Join(_output == Output.Markdown ? "<br>" : " / ", paragraphs);
                cells.Add(_output == Output.Markdown ? text.Replace("|", "\\|").Replace("\n", "<br>") : text.Replace("\n", " "));
                var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                for (var i = 1; i < span; i++) cells.Add("");
            }
            if (cells.Count > 0) rows.Add(cells);
        }
        if (rows.Count == 0) return "";

        var width = rows.Max(r => r.Count);
        foreach (var r in rows) while (r.Count < width) r.Add("");

        if (_output == Output.Text)
        {
            return string.Join("\n", rows.Select(r => string.Join("\t", r)));
        }

        // The header row is bold anyway, so "**Name**" there is just noise.
        var header = rows[0].Select(c => c.Length > 4 && c.StartsWith("**") && c.EndsWith("**") && !c[2..^2].Contains("**") ? c[2..^2] : c);
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", header)).Append(" |\n");
        sb.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');
        foreach (var r in rows.Skip(1)) sb.Append("| ").Append(string.Join(" | ", r)).Append(" |\n");
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------ inline text

    private string Inline(OpenXmlElement paragraph)
    {
        var segments = new List<Segment>();
        CollectSegments(paragraph, null, segments);
        return _output == Output.Markdown ? RenderMarkdown(segments) : string.Concat(segments.Select(s => s.IsImage ? "" : s.Text));
    }

    private void CollectSegments(OpenXmlElement element, string? link, List<Segment> into)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    AddRun(run, link, into);
                    break;
                case Hyperlink hyperlink:
                    var url = hyperlink.Id?.Value is { } relId
                        ? _main.HyperlinkRelationships.FirstOrDefault(r => r.Id == relId)?.Uri.ToString()
                        : null;
                    CollectSegments(hyperlink, url ?? link, into);
                    break;
                case DeletedRun or ParagraphProperties:
                    break;
                case InsertedRun or SimpleField or SdtRun or SdtContentRun or CustomXmlRun:
                    CollectSegments(child, link, into);
                    break;
            }
        }
    }

    private void AddRun(Run run, string? link, List<Segment> into)
    {
        var props = run.RunProperties;
        var bold = IsOn(props?.Bold);
        var italic = IsOn(props?.Italic);
        var strike = IsOn(props?.Strike) || IsOn(props?.DoubleStrike);
        var font = props?.RunFonts?.Ascii?.Value ?? "";
        var charStyle = StyleName(props?.RunStyle?.Val?.Value) ?? "";
        var code = charStyle.Contains("code", StringComparison.Ordinal)
            || font.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Menlo", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Mono", StringComparison.OrdinalIgnoreCase);

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    into.Add(new Segment(t.Text, bold, italic, strike, code, link));
                    break;
                case TabChar:
                    into.Add(new Segment("\t", bold, italic, strike, code, link));
                    break;
                case Break br when br.Type?.Value != BreakValues.Page && br.Type?.Value != BreakValues.Column:
                case CarriageReturn:
                    into.Add(new Segment("\n", false, false, false, false, null));
                    break;
                case NoBreakHyphen:
                    into.Add(new Segment("-", bold, italic, strike, code, link));
                    break;
                case Drawing drawing when _output == Output.Markdown:
                    if (ImageDataUri(drawing) is { } uri)
                    {
                        var alt = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>()
                            .FirstOrDefault()?.Description?.Value ?? "image";
                        into.Add(new Segment($"![{EscapeMarkdown(alt)}]({uri})", false, false, false, false, null, IsImage: true));
                    }
                    break;
            }
        }
    }

    private static bool IsOn(OnOffType? flag) => flag is not null && (flag.Val is null || flag.Val.Value);

    private string? ImageDataUri(Drawing drawing)
    {
        var embed = drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
        if (embed is null || _main.GetPartById(embed) is not ImagePart part) return null;
        using var stream = part.GetStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return $"data:{part.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
    }

    private static string RenderMarkdown(List<Segment> segments)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < segments.Count)
        {
            var s = segments[i];
            if (s.IsImage)
            {
                sb.Append(s.Text);
                i++;
                continue;
            }
            // Merge neighbours with the same formatting so "**a****b**" becomes "**ab**".
            var text = new StringBuilder(s.Text);
            var j = i + 1;
            while (j < segments.Count && !segments[j].IsImage && segments[j] with { Text = s.Text } == s)
            {
                text.Append(segments[j].Text);
                j++;
            }
            sb.Append(Format(text.ToString(), s));
            i = j;
        }
        return sb.ToString().Replace("\n", "  \n");
    }

    private static string Format(string text, Segment s)
    {
        if (text.Length == 0) return "";
        if (text.Trim().Length == 0) return text;

        // Keep surrounding spaces outside the markers: "** bold **" is not bold in Markdown.
        var lead = text[..(text.Length - text.TrimStart().Length)];
        var trail = text[text.TrimEnd().Length..];
        var core = text.Trim();

        core = s.Code ? "`" + core.Replace("`", "\\`") + "`" : EscapeMarkdown(core);
        if (!s.Code)
        {
            if (s.Strike) core = $"~~{core}~~";
            if (s.Italic) core = $"*{core}*";
            if (s.Bold) core = $"**{core}**";
        }
        if (s.Link is { } url) core = $"[{core}]({url.Replace(")", "%29").Replace(" ", "%20")})";
        return lead + core + trail;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*([-+]|\d+[.)])\s")]
    private static partial System.Text.RegularExpressions.Regex LooksLikeListMarker();

    internal static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '#' or '|' or '~') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
