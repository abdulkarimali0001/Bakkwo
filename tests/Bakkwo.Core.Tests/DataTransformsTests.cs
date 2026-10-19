using System.Text.Json.Nodes;
using Bakkwo.Core.Data;

namespace Bakkwo.Core.Tests;

public class DataTransformsTests
{
    [Fact]
    public void Csv_parser_handles_quotes_commas_and_newlines()
    {
        var rows = DataTransforms.ParseDelimited("name,quote\r\n\"Kim, Ali\",\"He said \"\"hi\"\"\nthen left\"\r\n", ',');
        Assert.Equal(2, rows.Count);
        Assert.Equal("Kim, Ali", rows[1][0]);
        Assert.Equal("He said \"hi\"\nthen left", rows[1][1]);
    }

    [Fact]
    public void Csv_to_json_infers_numbers_but_keeps_leading_zeros()
    {
        var node = DataTransforms.DelimitedToNode("id,zip,price,active\n1,007,12.5,true\n", ',');
        var row = node[0]!.AsObject();
        Assert.Equal(1L, row["id"]!.GetValue<long>());
        Assert.Equal("007", row["zip"]!.GetValue<string>());
        Assert.Equal(12.5, row["price"]!.GetValue<double>());
        Assert.True(row["active"]!.GetValue<bool>());
    }

    [Fact]
    public void Json_to_csv_flattens_nested_objects()
    {
        var json = DataTransforms.ParseJson("""[{"name":"A","address":{"city":"Sokcho"}},{"name":"B","tags":["x","y"]}]""");
        var csv = DataTransforms.NodeToDelimited(json, ',');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("name,address.city,tags.0,tags.1", lines[0]);
        Assert.Equal("A,Sokcho,,", lines[1]);
        Assert.Equal("B,,x,y", lines[2]);
    }

    [Fact]
    public void Csv_values_with_separators_are_quoted()
    {
        var csv = DataTransforms.NodeToDelimited(DataTransforms.ParseJson("""[{"a":"x,y","b":"say \"hi\""}]"""), ',');
        Assert.Contains("\"x,y\",\"say \"\"hi\"\"\"", csv);
    }

    [Fact]
    public void Yaml_to_json_keeps_types()
    {
        var node = DataTransforms.YamlToNode("""
            name: Bakkwo
            version: 1
            ratio: 0.5
            enabled: yes_but_string
            zip: "007"
            empty:
            list: [a, 2]
            """)!;
        Assert.Equal("Bakkwo", node["name"]!.GetValue<string>());
        Assert.Equal(1L, node["version"]!.GetValue<long>());
        Assert.Equal(0.5, node["ratio"]!.GetValue<double>());
        Assert.Equal("007", node["zip"]!.GetValue<string>());
        Assert.Null(node["empty"]);
        Assert.Equal(2L, node["list"]![1]!.GetValue<long>());
    }

    [Fact]
    public void Json_yaml_json_round_trip_is_lossless()
    {
        const string json = """{"name":"Ali","age":24,"score":3.8,"zip":"007","flag":"true","tags":["a","b"],"none":null,"nested":{"k":[1,2]}}""";
        var original = DataTransforms.ParseJson(json);
        var back = DataTransforms.YamlToNode(DataTransforms.NodeToYaml(original));
        Assert.True(JsonNode.DeepEquals(original, back), DataTransforms.ToJson(back));
    }

    [Fact]
    public void Xml_round_trip_keeps_attributes_and_repeated_elements()
    {
        const string xml = """<library><book id="1"><title>A</title></book><book id="2"><title>B</title></book></library>""";
        var node = DataTransforms.XmlToNode(xml);
        var books = node["library"]!["book"]!.AsArray();
        Assert.Equal(2, books.Count);
        Assert.Equal("2", books[1]!["@id"]!.GetValue<string>());

        var back = DataTransforms.NodeToXml(node);
        Assert.Contains("<book id=\"1\">", back);
        Assert.Contains("<title>B</title>", back);
        Assert.StartsWith("<?xml", back);
    }

    [Fact]
    public void Json_with_comments_and_trailing_commas_is_accepted()
    {
        var node = DataTransforms.ParseJson("{ // comment\n \"a\": 1, }");
        Assert.Equal(1, node!["a"]!.GetValue<int>());
    }
}
