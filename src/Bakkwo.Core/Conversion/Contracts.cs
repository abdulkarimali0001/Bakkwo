using Bakkwo.Core.Formats;

namespace Bakkwo.Core.Conversion;

/// <summary>User-facing knobs. Converters ignore the ones that don't apply to them.</summary>
public sealed record ConversionOptions
{
    /// <summary>1–100. Used by JPEG, WebP, PDF images and video (mapped to CRF).</summary>
    public int Quality { get; init; } = 90;

    /// <summary>Shrink images so neither side exceeds this many pixels. Never enlarges.</summary>
    public int? MaxDimension { get; init; }

    public bool Grayscale { get; init; }

    /// <summary>Where results go. Null means next to the original file.</summary>
    public string? OutputDirectory { get; init; }

    public static ConversionOptions Default { get; } = new();
}

/// <summary>
/// One hop in the format graph, e.g. yaml → json, served by one converter. A <paramref name="Terminal"/>
/// edge ends a route: the planner won't chain further from its result (video → GIF → PNG makes no sense).
/// </summary>
public sealed record ConversionEdge(string From, string To, IConverter Converter, int Cost = 1, bool Terminal = false)
{
    public override string ToString() => $"{From}→{To} ({Converter.Name})";
}

/// <summary>A single file-to-file job handed to a converter.</summary>
public sealed record ConversionStep(string InputPath, string OutputPath, FileFormat From, FileFormat To);

public interface IConverter
{
    string Name { get; }

    /// <summary>False when an external engine (FFmpeg) is missing; its edges are then left out of the graph.</summary>
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    /// <summary>How many conversions this converter may run at once (null = no limit). Heavy external tools use 1–2.</summary>
    int? MaxConcurrency { get; }

    IEnumerable<ConversionEdge> Edges { get; }

    /// <summary>
    /// Converts <see cref="ConversionStep.InputPath"/> into <see cref="ConversionStep.OutputPath"/>.
    /// The output path already exists as an empty placeholder file (or empty folder) and should be overwritten.
    /// </summary>
    Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct);
}

public abstract class ConverterBase : IConverter
{
    public abstract string Name { get; }
    public virtual bool IsAvailable => true;
    public virtual string? UnavailableReason => null;
    public virtual int? MaxConcurrency => null;
    public abstract IEnumerable<ConversionEdge> Edges { get; }

    public abstract Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct);

    protected ConversionEdge Edge(string from, string to, int cost = 1, bool terminal = false) => new(from, to, this, cost, terminal);
}

public sealed record ConversionResult(
    string InputPath,
    string? OutputPath,
    bool Success,
    string? Error,
    IReadOnlyList<ConversionEdge> Route,
    TimeSpan Elapsed)
{
    /// <summary>"yaml → json → csv"</summary>
    public string RouteDescription =>
        Route.Count == 0 ? "" : string.Join(" → ", new[] { Route[0].From }.Concat(Route.Select(e => e.To)));
}

/// <summary>Progress for one file in a batch. <see cref="Result"/> is set once that file is done.</summary>
public sealed record FileProgress(int Index, double Fraction, ConversionResult? Result);

/// <summary>Reports synchronously on the calling thread (unlike <see cref="Progress{T}"/>, which posts).</summary>
internal sealed class InlineProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}
