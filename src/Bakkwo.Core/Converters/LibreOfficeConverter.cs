using System.Collections.Concurrent;
using System.Diagnostics;
using Bakkwo.Core.Conversion;

namespace Bakkwo.Core.Converters;

/// <summary>
/// Word-family documents through LibreOffice running headless: DOC, DOT, RTF and ODT in and out,
/// and an exact-layout PDF from any of them. Optional, like FFmpeg: without LibreOffice these
/// edges simply aren't in the graph and DOCX ↔ Markdown/text still work through <see cref="WordConverter"/>.
/// </summary>
public sealed class LibreOfficeConverter : ConverterBase
{
    private static readonly string[] WordInputs = ["docx", "docm", "dotx", "doc", "dot", "rtf", "odt"];
    private static readonly string[] WordOutputs = ["pdf", "docx", "doc", "odt", "rtf"];

    private static readonly Dictionary<string, string> ExportFilters = new()
    {
        ["pdf"] = "pdf:writer_pdf_Export",
        ["docx"] = "docx:MS Word 2007 XML",
        ["doc"] = "doc:MS Word 97",
        ["odt"] = "odt:writer8",
        ["rtf"] = "rtf:Rich Text Format",
        ["txt"] = "txt:Text (encoded):UTF8",
        ["html"] = "html:XHTML Writer File:UTF8",
    };

    private static readonly Dictionary<string, string> ImportFilters = new()
    {
        ["html"] = "HTML (StarWriter)",   // open HTML as a Writer document so Word/PDF export works
        ["txt"] = "Text (encoded):UTF8",
    };

    private readonly Lazy<string?> _soffice;
    private readonly ConcurrentQueue<string> _profiles = new();

    public LibreOfficeConverter(string? sofficePath = null) =>
        _soffice = new Lazy<string?>(() => sofficePath is null
            ? LibreOfficeLocator.Find()
            : File.Exists(sofficePath) ? sofficePath : null);

    public override string Name => "LibreOffice";

    public override bool IsAvailable => _soffice.Value is not null;

    public override string? UnavailableReason => IsAvailable
        ? null
        : "LibreOffice not found — install it (macOS: brew install --cask libreoffice) for DOC/RTF/ODT and Word → PDF.";

    // Each conversion starts a LibreOffice process (~1 s and a few hundred MB), so two at a time.
    public override int? MaxConcurrency => 2;

    public override IEnumerable<ConversionEdge> Edges
    {
        get
        {
            foreach (var from in WordInputs)
            {
                foreach (var to in WordOutputs.Where(t => t != from)) yield return Edge(from, to, cost: 2);
                // DOCX-family → text/HTML is done in-process by WordConverter (keeps pictures); only older formats here.
                if (from is "doc" or "dot" or "rtf" or "odt")
                {
                    yield return Edge(from, "txt", cost: 2);
                    yield return Edge(from, "html", cost: 2);
                }
            }
            // HTML (also Markdown via HTML) and plain text into documents.
            foreach (var to in new[] { "pdf", "docx", "odt", "rtf", "doc" }) yield return Edge("html", to, cost: 2);
            yield return Edge("txt", "pdf", cost: 2);
        }
    }

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        var soffice = _soffice.Value ?? throw new InvalidOperationException(UnavailableReason);
        var work = ConversionEngine.CreateTempDir();
        var profile = RentProfile();
        try
        {
            // Copy to a plain name: LibreOffice names its output after the input and dislikes some characters.
            var input = Path.Combine(work, "input" + step.From.PrimaryExtension);
            File.Copy(step.InputPath, input);
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);

            var info = new ProcessStartInfo(soffice)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-env:UserInstallation=" + new Uri(profile).AbsoluteUri);
            foreach (var arg in new[] { "--headless", "--invisible", "--nologo", "--norestore", "--nolockcheck", "--nodefault" })
                info.ArgumentList.Add(arg);
            if (ImportFilters.TryGetValue(step.From.Id, out var infilter)) info.ArgumentList.Add("--infilter=" + infilter);
            info.ArgumentList.Add("--convert-to");
            info.ArgumentList.Add(ExportFilters[step.To.Id]);
            info.ArgumentList.Add("--outdir");
            info.ArgumentList.Add(outDir);
            info.ArgumentList.Add(input);

            progress?.Report(0.1);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start LibreOffice.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            using var kill = timeout.Token.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            });

            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested) throw new TimeoutException("LibreOffice took longer than 3 minutes.");

            var produced = Directory.EnumerateFiles(outDir).FirstOrDefault();
            if (produced is null || new FileInfo(produced).Length == 0)
            {
                var detail = ((await stderr) + (await stdout)).Trim();
                throw new InvalidOperationException(
                    $"LibreOffice couldn't convert this file to {step.To.Label}." + (detail.Length > 0 ? "\n" + detail : ""));
            }

            File.Copy(produced, step.OutputPath, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            _profiles.Enqueue(profile);
            ConversionEngine.TryDelete(work);
        }
    }

    /// <summary>
    /// Separate LibreOffice profiles let two conversions run side by side (one profile can only be
    /// used by one process). Profiles are reused within a run, so only the first conversion pays
    /// LibreOffice's first-start cost.
    /// </summary>
    private string RentProfile()
    {
        if (_profiles.TryDequeue(out var existing)) return existing;
        var dir = Path.Combine(Path.GetTempPath(), "bakkwo-libreoffice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class LibreOfficeLocator
{
    /// <summary>BAKKWO_SOFFICE, then the standard install locations, then PATH.</summary>
    public static string? Find()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BAKKWO_SOFFICE");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            "/Applications/LibreOffice.app/Contents/MacOS/soffice",
            Path.Combine(home, "Applications/LibreOffice.app/Contents/MacOS/soffice"),
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
            "/usr/bin/soffice",
            "/usr/lib/libreoffice/program/soffice",
            "/opt/homebrew/bin/soffice",
            "/usr/local/bin/soffice",
            "/snap/bin/libreoffice",
        };
        var exe = OperatingSystem.IsWindows() ? "soffice.exe" : "soffice";
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, exe)));
        return candidates.FirstOrDefault(File.Exists);
    }
}
