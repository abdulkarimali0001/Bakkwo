using System.Text;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;
using Bakkwo.Core.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Bakkwo.Core.Tests;

public class WordTests : IDisposable
{
    private const string Sample = """
        # 주간 보고서 Weekly Report

        Prepared by **Ali** with *Bakkwo*, see [the repo](https://example.com/bakkwo).

        ## Goals

        - 한국어 문서 (Korean)
        - Keep **bold** and `code`
           - nested item

        1. First step
        2. Second step

        | Item | Status |
        | --- | --- |
        | DOCX | done |

        > A quote

        ```
        dotnet test
        ```
        """;

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static FileFormat F(string id) => FormatCatalog.ById(id)!;

    private string WriteDocx(string markdown = Sample)
    {
        var path = _temp.File("report.docx");
        DocxWriter.FromMarkdown(markdown, path);
        return path;
    }

    [Fact]
    public void Written_docx_passes_the_OpenXml_validator()
    {
        var path = WriteDocx();
        using var doc = WordprocessingDocument.Open(path, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2016).Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Written_docx_uses_real_word_styles()
    {
        using var doc = WordprocessingDocument.Open(WriteDocx(), false);
        var styles = doc.MainDocumentPart!.Document.Body!.Descendants<ParagraphStyleId>().Select(s => s.Val!.Value).ToHashSet();
        Assert.Contains("Title", styles);
        Assert.Contains("Heading2", styles);
        Assert.Contains("ListParagraph", styles);
        Assert.Contains("Quote", styles);
        Assert.Contains("Code", styles);
        Assert.Single(doc.MainDocumentPart.Document.Body.Descendants<Table>());
        Assert.Single(doc.MainDocumentPart.HyperlinkRelationships);
    }

    [Fact]
    public void Markdown_round_trip_keeps_structure()
    {
        var md = DocxReader.ToMarkdown(WriteDocx());

        Assert.Contains("# 주간 보고서 Weekly Report", md);
        Assert.Contains("## Goals", md);
        Assert.Contains("Prepared by **Ali** with *Bakkwo*, see [the repo](https://example.com/bakkwo).", md);
        Assert.Contains("- 한국어 문서 (Korean)", md);
        Assert.Contains("- Keep **bold** and `code`", md);
        Assert.Contains("   - nested item", md);
        Assert.Contains("1. First step\n2. Second step", md);
        Assert.Contains("| Item | Status |", md);
        Assert.Contains("| DOCX | done |", md);
        Assert.Contains("> A quote", md);
        Assert.Contains("```\ndotnet test\n```", md);
    }

    [Fact]
    public void Plain_text_has_bullets_and_numbers()
    {
        var text = DocxReader.ToPlainText(WriteDocx());
        Assert.Contains("• 한국어 문서 (Korean)", text);
        Assert.Contains("1. First step", text);
        Assert.Contains("2. Second step", text);
        Assert.Contains("DOCX\tdone", text);
        Assert.DoesNotContain("**", text);
    }

    [Fact]
    public void Each_numbered_list_restarts()
    {
        var md = DocxReader.ToMarkdown(WriteDocx("1. a\n2. b\n\nText between.\n\n1. c\n2. d\n"));
        Assert.Contains("1. a\n2. b", md);
        Assert.Contains("1. c\n2. d", md);
    }

    [Fact]
    public void Headings_from_korean_word_are_recognised()
    {
        // Korean Word names heading styles "heading 1" but gives them ids like "1".
        var path = _temp.File("korean.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(
                new Style(new StyleName { Val = "heading 1" }) { Type = StyleValues.Paragraph, StyleId = "1" },
                new Style(new StyleName { Val = "heading 2" }) { Type = StyleValues.Paragraph, StyleId = "2" });
            main.Document = new Document(new Body(
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "1" }), new Run(new Text("제목"))),
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "2" }), new Run(new Text("소제목"))),
                new Paragraph(new Run(new Text("- not a list")))));
        }

        var md = DocxReader.ToMarkdown(path);
        Assert.Contains("# 제목", md);
        Assert.Contains("## 소제목", md);
        Assert.Contains("\\- not a list", md); // escaped so it stays a paragraph
    }

    [Fact]
    public void Pictures_survive_markdown_to_docx_and_back()
    {
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(20, 10))
        {
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, _temp.File("pic.png"));
        }
        var docx = _temp.File("pic.docx");
        DocxWriter.FromMarkdown("Look:\n\n![a small picture](pic.png)\n", docx, _temp.Path);

        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            Assert.Single(doc.MainDocumentPart!.ImageParts);
        }
        Assert.Contains("![a small picture](data:image/png;base64,", DocxReader.ToMarkdown(docx));
    }

    [Fact]
    public void Plain_text_becomes_docx_paragraphs()
    {
        var docx = _temp.File("notes.docx");
        DocxWriter.FromPlainText("first line\nsecond line\n\nnew paragraph\twith tab", docx);
        using var doc = WordprocessingDocument.Open(docx, false);
        var paragraphs = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
        Assert.Equal(2, paragraphs.Count);
        Assert.Single(paragraphs[0].Descendants<Break>());
        Assert.Single(paragraphs[1].Descendants<TabChar>());
    }

    [Fact]
    public async Task Engine_converts_docx_to_html_through_markdown()
    {
        var engine = new ConversionEngine(new ConverterRegistry()
            .Add(new WordConverter()).Add(new TextConverter()).Add(new LibreOfficeConverter("/nonexistent/soffice")));
        var docx = WriteDocx();

        var html = await engine.ConvertAsync(docx, F("html"));
        Assert.True(html.Success, html.Error);
        Assert.Equal("docx → md → html", html.RouteDescription);
        var content = await File.ReadAllTextAsync(html.OutputPath!);
        Assert.Contains("<table>", content);
        Assert.Contains("<strong>Ali</strong>", content);

        // Without LibreOffice there is no Word → PDF, and DOC isn't readable.
        Assert.Null(engine.Planner.FindRoute(F("docx"), F("pdf")));
        Assert.Null(engine.Planner.FindRoute(F("doc"), F("md")));
    }

    [Fact]
    public void Word_formats_are_detected()
    {
        Assert.Equal("docx", FormatCatalog.Detect("a.DOCX")?.Id);
        Assert.Equal("doc", FormatCatalog.Detect("old.doc")?.Id);
        Assert.Equal("docm", FormatCatalog.Detect("macro.docm")?.Id);
        Assert.Equal("dotx", FormatCatalog.Detect("letter.dotx")?.Id);
        Assert.Equal("rtf", FormatCatalog.Detect("notes.rtf")?.Id);
        Assert.Equal("odt", FormatCatalog.Detect("paper.odt")?.Id);
    }

    // ---------------------------------------------------------------- LibreOffice (skipped if not installed)

    private static readonly bool HasLibreOffice = LibreOfficeLocator.Find() is not null;

    [Fact]
    public async Task LibreOffice_makes_pdf_and_old_word_formats()
    {
        if (!HasLibreOffice) return;
        var engine = new ConversionEngine(ConverterRegistry.CreateDefault());
        var docx = WriteDocx();

        var pdf = await engine.ConvertAsync(docx, F("pdf"));
        Assert.True(pdf.Success, pdf.Error);
        Assert.StartsWith("%PDF", Encoding.Latin1.GetString((await File.ReadAllBytesAsync(pdf.OutputPath!))[..5]));

        var doc = await engine.ConvertAsync(docx, F("doc"));
        Assert.True(doc.Success, doc.Error);

        // DOC → Markdown goes through DOCX and keeps the structure, numbering included.
        var md = await engine.ConvertAsync(doc.OutputPath!, F("md"), new ConversionOptions { OutputDirectory = _temp.File("back") });
        Assert.True(md.Success, md.Error);
        Assert.Equal("doc → docx → md", md.RouteDescription);
        var text = await File.ReadAllTextAsync(md.OutputPath!);
        Assert.Contains("## Goals", text);
        Assert.Contains("1. First step\n2. Second step", text);
    }

    [Fact]
    public async Task LibreOffice_reads_rtf_and_odt()
    {
        if (!HasLibreOffice) return;
        var engine = new ConversionEngine(ConverterRegistry.CreateDefault());
        var rtf = _temp.Write("note.rtf", @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs24 Hello \b bold\b0  world.\par}");

        var odt = await engine.ConvertAsync(rtf, F("odt"));
        Assert.True(odt.Success, odt.Error);

        var txt = await engine.ConvertAsync(odt.OutputPath!, F("txt"));
        Assert.True(txt.Success, txt.Error);
        Assert.Contains("Hello bold world.", await File.ReadAllTextAsync(txt.OutputPath!));
    }
}
