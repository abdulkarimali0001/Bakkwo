using System.Diagnostics;
using Bakkwo.Core.Formats;

namespace Bakkwo.Core.Conversion;

/// <summary>Plans a route, runs each hop through temp files, and puts the result next to the original.</summary>
public sealed class ConversionEngine
{
    public ConversionEngine(ConverterRegistry registry)
    {
        Registry = registry;
        Planner = new RoutePlanner(registry);
    }

    public ConverterRegistry Registry { get; }
    public RoutePlanner Planner { get; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<IConverter, SemaphoreSlim> _limits = new();

    public async Task<ConversionResult> ConvertAsync(
        string inputPath,
        FileFormat target,
        ConversionOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        options ??= ConversionOptions.Default;
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ConversionEdge> route = [];
        string? finalOutput = null;
        string? tempDir = null;

        try
        {
            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
                throw new FileNotFoundException($"File not found: {inputPath}");

            var from = FormatCatalog.Detect(inputPath)
                ?? throw new NotSupportedException($"Unknown file type: {Path.GetFileName(inputPath)}");
            route = Planner.FindRoute(from, target)
                ?? throw new NotSupportedException($"Can't convert {from.DisplayName} to {target.DisplayName}.");

            // Same format (shrink/compress): "clip (720p).mp4", never next to an identically named file.
            var stem = from.Id == target.Id ? $"{FormatCatalog.StemOf(inputPath)} ({SameFormatSuffix(options)})" : null;
            finalOutput = OutputPaths.Reserve(inputPath, target, options, stem);

            var current = inputPath;
            var currentFormat = from;
            for (var i = 0; i < route.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var edge = route[i];
                var nextFormat = FormatCatalog.ById(edge.To)!;
                var isLast = i == route.Count - 1;
                var output = isLast
                    ? finalOutput
                    : Path.Combine(tempDir ??= CreateTempDir(), $"step{i}{nextFormat.PrimaryExtension}");

                var stepIndex = i;
                var stepProgress = progress is null
                    ? null
                    : new InlineProgress(v => progress.Report((stepIndex + Math.Clamp(v, 0, 1)) / route.Count));

                var limit = edge.Converter.MaxConcurrency is int max
                    ? _limits.GetOrAdd(edge.Converter, _ => new SemaphoreSlim(max))
                    : null;
                if (limit is not null) await limit.WaitAsync(ct);
                try
                {
                    await edge.Converter.ConvertAsync(new ConversionStep(current, output, currentFormat, nextFormat), options, stepProgress, ct);
                }
                finally
                {
                    limit?.Release();
                }
                current = output;
                currentFormat = nextFormat;
            }

            progress?.Report(1);
            return new ConversionResult(inputPath, finalOutput, true, null, route, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            TryDelete(finalOutput);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(finalOutput);
            return new ConversionResult(inputPath, null, false, ex.Message, route, stopwatch.Elapsed);
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    /// <summary>Converts many files in parallel. Results come back in input order.</summary>
    public async Task<IReadOnlyList<ConversionResult>> ConvertManyAsync(
        IReadOnlyList<string> inputs,
        FileFormat target,
        ConversionOptions? options = null,
        IProgress<FileProgress>? progress = null,
        CancellationToken ct = default)
    {
        var results = new ConversionResult[inputs.Count];
        // Heavy converters (FFmpeg, LibreOffice) also limit themselves via MaxConcurrency.
        var parallel = Math.Max(1, Environment.ProcessorCount - 1);
        using var gate = new SemaphoreSlim(parallel);

        var tasks = inputs.Select(async (input, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var fileProgress = progress is null
                    ? null
                    : new InlineProgress(v => progress.Report(new FileProgress(index, v, null)));
                var result = await Task.Run(() => ConvertAsync(input, target, options, fileProgress, ct), ct);
                results[index] = result;
                progress?.Report(new FileProgress(index, 1, result));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    internal static string SameFormatSuffix(ConversionOptions options) => options.MaxDimension switch
    {
        3840 => "4K",
        1920 => "1080p",
        1280 => "720p",
        int px when px > 0 => $"{px}px",
        _ => "compressed",
    };

    internal static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bakkwo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
