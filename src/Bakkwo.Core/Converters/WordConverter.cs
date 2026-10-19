using System.Text;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Word;

namespace Bakkwo.Core.Converters;

/// <summary>
/// Word documents with no extra software: DOCX/DOCM/DOTX → Markdown or text, and Markdown or text
/// → DOCX. The planner chains the rest (DOCX → Markdown → HTML, HTML → text → DOCX).
/// </summary>
public sealed class WordConverter : ConverterBase
{
    private static readonly string[] OpenXmlInputs = ["docx", "docm", "dotx"];

    public override string Name => "Word";

    public override IEnumerable<ConversionEdge> Edges =>
        OpenXmlInputs.SelectMany(f => new[] { Edge(f, "md"), Edge(f, "txt") })
            .Concat([Edge("md", "docx"), Edge("txt", "docx")]);

    public override Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(async () =>
        {
            switch (step.From.Id, step.To.Id)
            {
                case ("docx" or "docm" or "dotx", "md"):
                    await File.WriteAllTextAsync(step.OutputPath, DocxReader.ToMarkdown(step.InputPath), new UTF8Encoding(false), ct);
                    break;
                case ("docx" or "docm" or "dotx", "txt"):
                    await File.WriteAllTextAsync(step.OutputPath, DocxReader.ToPlainText(step.InputPath), new UTF8Encoding(false), ct);
                    break;
                case ("md", "docx"):
                    var markdown = await File.ReadAllTextAsync(step.InputPath, ct);
                    DocxWriter.FromMarkdown(markdown, step.OutputPath, Path.GetDirectoryName(Path.GetFullPath(step.InputPath)));
                    break;
                case ("txt", "docx"):
                    DocxWriter.FromPlainText(await File.ReadAllTextAsync(step.InputPath, ct), step.OutputPath,
                        Path.GetFileNameWithoutExtension(step.InputPath));
                    break;
                default:
                    throw new NotSupportedException($"{step.From.Id} → {step.To.Id} is not a Word conversion.");
            }
            progress?.Report(1);
        }, ct);
}
