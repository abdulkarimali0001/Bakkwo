using System.Text;
using Bakkwo.Core.Conversion;
using Bakkwo.Core.Data;

namespace Bakkwo.Core.Converters;

/// <summary>
/// JSON is the hub: every data format converts to and from JSON, and the route planner
/// chains two hops for the rest (YAML → JSON → CSV).
/// </summary>
public sealed class DataConverter : ConverterBase
{
    private static readonly string[] Spokes = ["yaml", "csv", "tsv", "xml"];

    public override string Name => "Data";

    public override IEnumerable<ConversionEdge> Edges =>
        Spokes.Select(s => Edge(s, "json")).Concat(Spokes.Select(s => Edge("json", s)));

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(step.InputPath, ct);

        var output = (step.From.Id, step.To.Id) switch
        {
            ("yaml", "json") => DataTransforms.ToJson(DataTransforms.YamlToNode(text)),
            ("csv", "json") => DataTransforms.ToJson(DataTransforms.DelimitedToNode(text, ',')),
            ("tsv", "json") => DataTransforms.ToJson(DataTransforms.DelimitedToNode(text, '\t')),
            ("xml", "json") => DataTransforms.ToJson(DataTransforms.XmlToNode(text)),
            ("json", "yaml") => DataTransforms.NodeToYaml(DataTransforms.ParseJson(text)),
            ("json", "csv") => DataTransforms.NodeToDelimited(DataTransforms.ParseJson(text), ','),
            ("json", "tsv") => DataTransforms.NodeToDelimited(DataTransforms.ParseJson(text), '\t'),
            ("json", "xml") => DataTransforms.NodeToXml(DataTransforms.ParseJson(text)),
            _ => throw new NotSupportedException($"{step.From.Id} → {step.To.Id} is not a data conversion."),
        };

        // A BOM makes Excel open UTF-8 CSV files with Korean text correctly.
        var withBom = step.To.Id is "csv" or "tsv";
        await File.WriteAllTextAsync(step.OutputPath, output, new UTF8Encoding(withBom), ct);
        progress?.Report(1);
    }
}
