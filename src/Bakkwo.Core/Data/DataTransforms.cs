using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Bakkwo.Core.Data;

/// <summary>
/// Structured-data conversions. Everything goes through a JSON tree (<see cref="JsonNode"/>),
/// so each format needs only a reader and a writer, and the route planner chains them.
/// </summary>
public static class DataTransforms
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep 한글, Kiswahili, emoji readable
    };

    public static JsonNode? ParseJson(string text) =>
        JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

    public static string ToJson(JsonNode? node) => node?.ToJsonString(PrettyJson) ?? "null";

    // ---------------------------------------------------------------- YAML

    public static JsonNode? YamlToNode(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Count switch
        {
            0 => null,
            1 => FromYaml(stream.Documents[0].RootNode),
            // Multi-document YAML ("---" separated) becomes an array of documents.
            _ => new JsonArray(stream.Documents.Select(d => FromYaml(d.RootNode)).ToArray()),
        };
    }

    public static string NodeToYaml(JsonNode? node)
    {
        var serializer = new SerializerBuilder()
            .WithQuotingNecessaryStrings() // "007" and "true" stay strings
            .Build();
        return serializer.Serialize(ToPlain(node));
    }

    private static JsonNode? FromYaml(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                var obj = new JsonObject();
                foreach (var pair in map.Children)
                {
                    var key = pair.Key is YamlScalarNode s ? s.Value ?? "" : pair.Key.ToString();
                    obj[key] = FromYaml(pair.Value);
                }
                return obj;
            case YamlSequenceNode seq:
                return new JsonArray(seq.Children.Select(FromYaml).ToArray());
            case YamlScalarNode scalar:
                var value = scalar.Value;
                return scalar.Style is ScalarStyle.Plain or ScalarStyle.Any
                    ? InferScalar(value, emptyIsNull: true)
                    : JsonValue.Create(value ?? "");
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- CSV / TSV

    public static JsonNode DelimitedToNode(string text, char separator)
    {
        var rows = ParseDelimited(text, separator);
        var result = new JsonArray();
        if (rows.Count == 0) return result;

        var headers = rows[0]
            .Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"column{i + 1}" : h.Trim())
            .ToList();
        // Duplicate headers would overwrite each other; number them instead.
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < headers.Count; i++)
        {
            if (seen.TryGetValue(headers[i], out var count))
            {
                seen[headers[i]] = count + 1;
                headers[i] = $"{headers[i]}_{count + 1}";
            }
            else seen[headers[i]] = 1;
        }

        foreach (var row in rows.Skip(1))
        {
            var obj = new JsonObject();
            for (var i = 0; i < headers.Count; i++)
            {
                obj[headers[i]] = InferScalar(i < row.Count ? row[i] : "", emptyIsNull: false);
            }
            result.Add(obj);
        }
        return result;
    }

    public static string NodeToDelimited(JsonNode? node, char separator)
    {
        List<JsonNode?> records = node switch
        {
            JsonArray array => array.ToList(),
            // {"items": [...]} — use the one array of records inside the object.
            JsonObject obj when obj.Count == 1 && obj.First().Value is JsonArray inner => inner.ToList(),
            null => new List<JsonNode?>(),
            _ => new List<JsonNode?> { node },
        };

        var flatRows = new List<Dictionary<string, string>>();
        var headers = new List<string>();
        var known = new HashSet<string>();
        foreach (var record in records)
        {
            var flat = new Dictionary<string, string>();
            if (record is JsonObject or JsonArray) Flatten(record, "", flat);
            else flat["value"] = ScalarText(record);

            foreach (var key in flat.Keys.Where(known.Add)) headers.Add(key);
            flatRows.Add(flat);
        }

        var sb = new StringBuilder();
        sb.Append(string.Join(separator, headers.Select(h => Quote(h, separator)))).Append("\r\n");
        foreach (var row in flatRows)
        {
            sb.Append(string.Join(separator, headers.Select(h => Quote(row.GetValueOrDefault(h, ""), separator)))).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>RFC 4180 parser: quoted fields, doubled quotes, and line breaks inside quotes.</summary>
    public static List<List<string>> ParseDelimited(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }
        void EndRow()
        {
            EndField();
            if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row); // skip blank lines
            row = [];
        }

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == separator) EndField();
            else if (c == '\n') EndRow();
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj) Flatten(value, prefix.Length == 0 ? key : $"{prefix}.{key}", into);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++) Flatten(array[i], prefix.Length == 0 ? $"{i}" : $"{prefix}.{i}", into);
                break;
            default:
                into[prefix.Length == 0 ? "value" : prefix] = ScalarText(node);
                break;
        }
    }

    private static string Quote(string value, char separator)
    {
        var needsQuotes = value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    // ---------------------------------------------------------------- XML

    public static JsonNode XmlToNode(string text)
    {
        var root = XDocument.Parse(text).Root ?? throw new FormatException("The XML has no root element.");
        return new JsonObject { [XmlConvert.DecodeName(root.Name.LocalName)] = ElementToNode(root) };
    }

    public static string NodeToXml(JsonNode? node)
    {
        // {"library": {...}} → <library>…</library>; anything else is wrapped in <root>.
        var root = node is JsonObject { Count: 1 } single && single.First().Value is not JsonArray
            ? BuildElement(single.First().Key, single.First().Value)
            : BuildElement("root", node);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root + "\n";
    }

    private static JsonNode? ElementToNode(XElement element)
    {
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        var children = element.Elements().ToList();

        if (attributes.Count == 0 && children.Count == 0)
        {
            return element.IsEmpty ? null : JsonValue.Create(element.Value);
        }

        var obj = new JsonObject();
        foreach (var attribute in attributes)
        {
            obj["@" + XmlConvert.DecodeName(attribute.Name.LocalName)] = attribute.Value;
        }
        foreach (var group in children.GroupBy(c => XmlConvert.DecodeName(c.Name.LocalName)))
        {
            var items = group.ToList();
            obj[group.Key] = items.Count == 1
                ? ElementToNode(items[0])
                : new JsonArray(items.Select(ElementToNode).ToArray());
        }
        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (text.Length > 0) obj["#text"] = text;
        return obj;
    }

    private static XElement BuildElement(string name, JsonNode? node)
    {
        var element = new XElement(XmlName(name));
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key.StartsWith('@')) element.SetAttributeValue(XmlName(key[1..]), ScalarText(value));
                    else if (key == "#text") element.Add(new XText(ScalarText(value)));
                    else if (value is JsonArray array) foreach (var item in array) element.Add(BuildElement(key, item));
                    else element.Add(BuildElement(key, value));
                }
                break;
            case JsonArray array:
                foreach (var item in array) element.Add(BuildElement("item", item));
                break;
            case null:
                break;
            default:
                element.Value = ScalarText(node);
                break;
        }
        return element;
    }

    private static string XmlName(string name) => name.Length == 0 ? "_" : XmlConvert.EncodeLocalName(name);

    // ---------------------------------------------------------------- helpers

    /// <summary>Turns untyped text into a JSON number, boolean or null where that's unambiguous.</summary>
    internal static JsonNode? InferScalar(string? text, bool emptyIsNull)
    {
        if (text is null) return null;
        if (text.Length == 0) return emptyIsNull ? null : JsonValue.Create("");
        if (emptyIsNull && text is "~" or "null" or "Null" or "NULL") return null;
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(true);
        if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(false);

        // "007", "+82 10…" and "1e5x" stay strings: leading zeros usually mean an ID or postcode.
        var looksNumeric = text.Length > 0 && (char.IsDigit(text[0]) || (text[0] == '-' && text.Length > 1 && char.IsDigit(text[1])));
        var digits = text.TrimStart('-');
        var leadingZero = digits.Length > 1 && digits[0] == '0' && digits[1] != '.';
        if (looksNumeric && !leadingZero)
        {
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) return JsonValue.Create(d);
        }
        return JsonValue.Create(text);
    }

    private static string ScalarText(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    /// <summary>JSON tree → plain dictionaries/lists so YamlDotNet can serialise it.</summary>
    private static object? ToPlain(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                var dict = new Dictionary<string, object?>();
                foreach (var (key, value) in obj) dict[key] = ToPlain(value);
                return dict;
            case JsonArray array:
                return array.Select(ToPlain).ToList();
            case JsonValue value:
                return value.GetValueKind() switch
                {
                    JsonValueKind.String => (object?)value.GetValue<string>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when value.TryGetValue<long>(out var l) => l,
                    JsonValueKind.Number => value.GetValue<double>(),
                    _ => null,
                };
            default:
                return null;
        }
    }
}
