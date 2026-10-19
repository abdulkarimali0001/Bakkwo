using System.Formats.Tar;
using System.IO.Compression;
using Bakkwo.Core.Conversion;

namespace Bakkwo.Core.Converters;

/// <summary>ZIP, TAR and TAR.GZ: extract to a folder, or repack between them.</summary>
public sealed class ArchiveConverter : ConverterBase
{
    private static readonly string[] Archives = ["zip", "tar", "tgz"];

    public override string Name => "Archives";

    public override IEnumerable<ConversionEdge> Edges =>
        Archives.Select(a => Edge(a, "folder"))
            .Concat(from f in Archives from t in Archives where f != t select Edge(f, t));

    public override Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (step.To.Id == "folder")
            {
                Extract(step.InputPath, step.From.Id, step.OutputPath);
            }
            else
            {
                var temp = ConversionEngine.CreateTempDir();
                try
                {
                    Extract(step.InputPath, step.From.Id, temp);
                    progress?.Report(0.5);
                    ct.ThrowIfCancellationRequested();
                    Pack(temp, step.To.Id, step.OutputPath);
                }
                finally
                {
                    ConversionEngine.TryDelete(temp);
                }
            }
            progress?.Report(1);
        }, ct);

    /// <summary>Puts files and folders into one ZIP; folders keep their name as the top-level entry.</summary>
    public static Task ZipManyAsync(IReadOnlyList<string> paths, string outputZip, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var stream = new FileStream(outputZip, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string Unique(string name)
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                var candidate = name;
                for (var n = 1; !used.Add(candidate); n++) candidate = $"{stem} ({n}){ext}";
                return candidate;
            }

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(trimmed))
                {
                    var root = Unique(Path.GetFileName(trimmed));
                    foreach (var file in Directory.EnumerateFiles(trimmed, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(trimmed, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"{root}/{relative}", CompressionLevel.Optimal);
                    }
                }
                else
                {
                    zip.CreateEntryFromFile(trimmed, Unique(Path.GetFileName(trimmed)), CompressionLevel.Optimal);
                }
            }
        }, ct);

    // .NET's extractors refuse entries that would escape the destination folder ("zip slip").
    private static void Extract(string archive, string format, string destination)
    {
        Directory.CreateDirectory(destination);
        switch (format)
        {
            case "zip":
                ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
                break;
            case "tar":
                TarFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
                break;
            case "tgz":
                using (var file = File.OpenRead(archive))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                {
                    TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
                }
                break;
            default:
                throw new NotSupportedException($"Can't extract {format}.");
        }
    }

    private static void Pack(string sourceDir, string format, string output)
    {
        using var file = new FileStream(output, FileMode.Create);
        switch (format)
        {
            case "zip":
                ZipFile.CreateFromDirectory(sourceDir, file, CompressionLevel.Optimal, includeBaseDirectory: false);
                break;
            case "tar":
                TarFile.CreateFromDirectory(sourceDir, file, includeBaseDirectory: false);
                break;
            case "tgz":
                using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                {
                    TarFile.CreateFromDirectory(sourceDir, gzip, includeBaseDirectory: false);
                }
                break;
            default:
                throw new NotSupportedException($"Can't create {format}.");
        }
    }
}
