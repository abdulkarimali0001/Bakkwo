using Bakkwo.Core.Conversion;
using Bakkwo.Core.Converters;
using Bakkwo.Core.Formats;

namespace Bakkwo.Cli;

internal static class Program
{
    private const string Usage = """
        bakkwo — offline file converter

        Usage:
          bakkwo convert <files...> --to <format> [options]
          bakkwo zip <files or folders...> [-o <output.zip>]
          bakkwo pdf <images...> -o <output.pdf>        merge images into one PDF
          bakkwo targets <file>                          what a file can become
          bakkwo route <from> <to>                       show the conversion chain
          bakkwo formats                                 list every format

        Options for convert:
          -o, --out <dir>       write results here instead of next to the originals
          -q, --quality <1-100> JPEG/WebP/PDF/video quality (default 90)
          --max <px>            shrink images so no side is larger than this
          --gray                convert images to grayscale

        Examples:
          bakkwo convert photo.png --to webp -q 80
          bakkwo convert *.csv --to json -o converted
          bakkwo convert notes.md --to txt
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var engine = new ConversionEngine(ConverterRegistry.CreateDefault());
        try
        {
            return args[0] switch
            {
                "convert" => await Convert(engine, args[1..]),
                "zip" => await Zip(args[1..]),
                "pdf" => await MergePdf(args[1..]),
                "targets" => Targets(engine, args[1..]),
                "route" => Route(engine, args[1..]),
                "formats" => Formats(engine),
                _ => Fail($"Unknown command '{args[0]}'. Run 'bakkwo help'."),
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<int> Convert(ConversionEngine engine, string[] args)
    {
        var parsed = Parse(args);
        var target = parsed.Get("to") is { } to
            ? FormatCatalog.ById(to) ?? throw new ArgumentException($"Unknown format '{to}'. Run 'bakkwo formats'.")
            : throw new ArgumentException("Missing --to <format>.");
        if (parsed.Files.Count == 0) throw new ArgumentException("No input files.");

        var options = parsed.Options();
        var results = await engine.ConvertManyAsync(parsed.Files, target, options);

        foreach (var r in results)
        {
            if (r.Success)
            {
                var via = r.Route.Count > 1 ? $"  [{r.RouteDescription}]" : "";
                Console.WriteLine($"✔ {Path.GetFileName(r.InputPath)} → {r.OutputPath}{via}  {r.Elapsed.TotalMilliseconds:0} ms");
            }
            else
            {
                Console.Error.WriteLine($"✘ {Path.GetFileName(r.InputPath)}: {r.Error}");
            }
        }
        return results.All(r => r.Success) ? 0 : 1;
    }

    private static async Task<int> Zip(string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Files.Count == 0) throw new ArgumentException("Nothing to zip.");
        var output = parsed.Get("o") ?? parsed.Get("out")
            ?? OutputPaths.Reserve(parsed.Files[0], FormatCatalog.ById("zip")!, ConversionOptions.Default,
                parsed.Files.Count == 1 ? null : "Archive");
        await ArchiveConverter.ZipManyAsync(parsed.Files, output);
        Console.WriteLine($"✔ {output}");
        return 0;
    }

    private static async Task<int> MergePdf(string[] args)
    {
        var parsed = Parse(args);
        var output = parsed.Get("o") ?? parsed.Get("out") ?? throw new ArgumentException("Missing -o <output.pdf>.");
        if (parsed.Files.Count == 0) throw new ArgumentException("No images given.");
        await ImageToPdfConverter.MergeAsync(parsed.Files, output, parsed.Options());
        Console.WriteLine($"✔ {output} ({parsed.Files.Count} pages)");
        return 0;
    }

    private static int Targets(ConversionEngine engine, string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage: bakkwo targets <file>");
        var format = FormatCatalog.Detect(args[0]) ?? throw new ArgumentException("Unknown file type.");
        Console.WriteLine($"{format.DisplayName} can become: {string.Join(", ", engine.Planner.TargetsFor(format).Select(f => f.Id))}");
        return 0;
    }

    private static int Route(ConversionEngine engine, string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: bakkwo route <from> <to>");
        var from = FormatCatalog.ById(args[0]) ?? throw new ArgumentException($"Unknown format '{args[0]}'.");
        var to = FormatCatalog.ById(args[1]) ?? throw new ArgumentException($"Unknown format '{args[1]}'.");
        var route = engine.Planner.FindRoute(from, to);
        if (route is null) return Fail($"No route from {from.Id} to {to.Id}.");
        foreach (var edge in route) Console.WriteLine($"  {edge.From,-6} → {edge.To,-6}  {edge.Converter.Name}");
        return 0;
    }

    private static int Formats(ConversionEngine engine)
    {
        foreach (var group in FormatCatalog.All.GroupBy(f => f.Category))
        {
            Console.WriteLine($"{group.Key}:");
            foreach (var f in group) Console.WriteLine($"  {f.Id,-7} {f.DisplayName,-22} {string.Join(" ", f.Extensions)}");
        }
        foreach (var c in engine.Registry.Converters.Where(c => !c.IsAvailable))
        {
            Console.WriteLine($"\nNote: {c.UnavailableReason}");
        }
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    // ------------------------------------------------------------ arguments

    private sealed class Arguments
    {
        public List<string> Files { get; } = [];
        public Dictionary<string, string?> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string name) => Flags.GetValueOrDefault(name);

        public ConversionOptions Options()
        {
            var quality = Get("q") ?? Get("quality");
            var max = Get("max");
            return new ConversionOptions
            {
                Quality = quality is null ? 90 : ParseNumber(quality, "quality"),
                MaxDimension = max is null ? null : ParseNumber(max, "max"),
                Grayscale = Flags.ContainsKey("gray") || Flags.ContainsKey("grayscale"),
                OutputDirectory = Get("out") ?? Get("o"),
            };
        }

        private static int ParseNumber(string text, string option) =>
            int.TryParse(text, out var value) ? value : throw new ArgumentException($"--{option} expects a number, got '{text}'.");
    }

    private static readonly HashSet<string> Switches = new(StringComparer.OrdinalIgnoreCase) { "gray", "grayscale" };

    private static Arguments Parse(string[] args)
    {
        var parsed = new Arguments();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-') && arg.Length > 1 && !File.Exists(arg))
            {
                var name = arg.TrimStart('-');
                if (Switches.Contains(name)) parsed.Flags[name] = null;
                else if (i + 1 < args.Length) parsed.Flags[name] = args[++i];
                else throw new ArgumentException($"Option {arg} needs a value.");
            }
            else
            {
                parsed.Files.Add(arg);
            }
        }
        return parsed;
    }
}
