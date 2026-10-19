using Bakkwo.Core.Formats;

namespace Bakkwo.Core.Conversion;

/// <summary>
/// Treats formats as nodes and converter edges as weighted links, and finds the cheapest chain
/// with Dijkstra's algorithm. That is how "YAML → CSV" works without a YAML-to-CSV converter:
/// the planner routes it through JSON.
/// </summary>
public sealed class RoutePlanner(ConverterRegistry registry)
{
    /// <summary>Longest chain the planner will build. Every hop costs time and can lose detail.</summary>
    public int MaxSteps { get; init; } = 3;

    private readonly record struct Visit(int Cost, int Steps, ConversionEdge? Via);

    public IReadOnlyList<ConversionEdge>? FindRoute(FileFormat from, FileFormat to)
    {
        if (from.Id == to.Id) return null;

        var visits = Explore(from.Id);
        if (!visits.ContainsKey(to.Id)) return null;

        var route = new List<ConversionEdge>();
        var node = to.Id;
        while (visits[node].Via is { } edge)
        {
            route.Add(edge);
            node = edge.From;
        }
        route.Reverse();
        return route;
    }

    /// <summary>Every format reachable from <paramref name="from"/>, same-category formats first.</summary>
    public IReadOnlyList<FileFormat> TargetsFor(FileFormat from)
    {
        var visits = Explore(from.Id);
        var order = FormatCatalog.All.Select((f, i) => (f.Id, i)).ToDictionary(x => x.Id, x => x.i);

        return visits
            .Where(kv => kv.Key != from.Id)
            .Select(kv => (Format: FormatCatalog.ById(kv.Key), kv.Value.Cost))
            .Where(x => x.Format is not null)
            .OrderBy(x => x.Format!.Category == from.Category ? 0 : 1)
            .ThenBy(x => x.Cost)
            .ThenBy(x => order.GetValueOrDefault(x.Format!.Id, int.MaxValue))
            .Select(x => x.Format!)
            .ToList();
    }

    /// <summary>Formats that every one of <paramref name="sources"/> can be converted to, in the first source's order.</summary>
    public IReadOnlyList<FileFormat> CommonTargets(IEnumerable<FileFormat> sources)
    {
        var distinct = sources.DistinctBy(f => f.Id).ToList();
        if (distinct.Count == 0) return [];

        var result = TargetsFor(distinct[0]).ToList();
        foreach (var other in distinct.Skip(1))
        {
            var reachable = TargetsFor(other).Select(f => f.Id).ToHashSet();
            result.RemoveAll(f => !reachable.Contains(f.Id));
        }
        return result;
    }

    private Dictionary<string, Visit> Explore(string from)
    {
        var graph = registry.Converters
            .Where(c => c.IsAvailable)
            .SelectMany(c => c.Edges)
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.ToList());

        var visits = new Dictionary<string, Visit> { [from] = new(0, 0, null) };
        var queue = new PriorityQueue<string, (int Cost, int Steps)>();
        queue.Enqueue(from, (0, 0));

        while (queue.TryDequeue(out var node, out var priority))
        {
            var current = visits[node];
            if (priority != (current.Cost, current.Steps)) continue; // stale queue entry
            if (current.Steps >= MaxSteps) continue;
            if (!graph.TryGetValue(node, out var edges)) continue;

            foreach (var edge in edges)
            {
                var cost = current.Cost + edge.Cost;
                var steps = current.Steps + 1;
                if (!visits.TryGetValue(edge.To, out var known)
                    || cost < known.Cost
                    || (cost == known.Cost && steps < known.Steps))
                {
                    visits[edge.To] = new Visit(cost, steps, edge);
                    queue.Enqueue(edge.To, (cost, steps));
                }
            }
        }
        return visits;
    }
}
