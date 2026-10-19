using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Bakkwo.Core.Conversion;
using Markdig;

namespace Bakkwo.Core.Converters;

/// <summary>Markdown → HTML, text → HTML and HTML → text. Markdown → text is planned via HTML.</summary>
public sealed partial class TextConverter : ConverterBase
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public override string Name => "Text";

    public override IEnumerable<ConversionEdge> Edges =>
    [
        Edge("md", "html"),
        Edge("txt", "html"),
        Edge("html", "txt"),
    ];

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(step.InputPath, ct);
        var title = Path.GetFileNameWithoutExtension(step.InputPath);

        var output = (step.From.Id, step.To.Id) switch
        {
            ("md", "html") => MarkdownToHtml(text, title),
            ("txt", "html") => TextToHtml(text, title),
            ("html", "txt") => HtmlToText(text),
            _ => throw new NotSupportedException($"{step.From.Id} → {step.To.Id} is not a text conversion."),
        };

        await File.WriteAllTextAsync(step.OutputPath, output, new UTF8Encoding(false), ct);
        progress?.Report(1);
    }

    public static string MarkdownToHtml(string markdown, string fallbackTitle)
    {
        var heading = markdown.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# "));
        var title = heading is null ? fallbackTitle : heading[2..].Trim();
        return Page(title, Markdown.ToHtml(markdown, Pipeline));
    }

    public static string TextToHtml(string text, string title)
    {
        var paragraphs = BlankLines().Split(text.Replace("\r\n", "\n").Trim())
            .Where(p => p.Length > 0)
            .Select(p => "<p>" + WebUtility.HtmlEncode(p).Replace("\n", "<br>\n") + "</p>");
        return Page(title, string.Join("\n", paragraphs));
    }

    public static string HtmlToText(string html)
    {
        var text = HiddenBlocks().Replace(html, "");
        text = LineBreak().Replace(text, "\n");
        text = ListItem().Replace(text, "• ");
        text = ListItemEnd().Replace(text, "\n");
        text = BlockEnd().Replace(text, "\n\n");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()));
        text = ManyBlankLines().Replace(text, "\n\n");
        return text.Trim() + "\n";
    }

    private static string Page(string title, string body) => $$"""
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          :root { color-scheme: light dark; }
          body { font: 16px/1.6 -apple-system, "Segoe UI", system-ui, sans-serif; max-width: 46rem; margin: 2.5rem auto; padding: 0 1rem; }
          pre, code { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: .92em; }
          pre { padding: 1rem; overflow-x: auto; border-radius: 8px; background: rgba(127,127,127,.12); }
          table { border-collapse: collapse; }
          th, td { border: 1px solid rgba(127,127,127,.4); padding: .35rem .6rem; }
          img { max-width: 100%; }
          blockquote { margin-left: 0; padding-left: 1rem; border-left: 3px solid rgba(127,127,127,.5); }
        </style>
        </head>
        <body>
        {{body}}
        </body>
        </html>
        """;

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenBlocks();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"</li\s*>\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemEnd();

    [GeneratedRegex(@"</(p|div|h[1-6]|ul|ol|table|tr|pre|blockquote|section|article)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();
}
