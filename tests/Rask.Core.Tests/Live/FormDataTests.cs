using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class FormDataTests
{
    [Fact]
    public void Get_MissingKey_ReturnsEmptyString()
    {
        var data = new FormData(new Dictionary<string, string>());

        Assert.Equal(string.Empty, data.Get("nope"));
    }

    [Fact]
    public void Get_PresentKey_ReturnsValue()
    {
        var data = new FormData(new Dictionary<string, string> { ["a"] = "1" });

        Assert.Equal("1", data.Get("a"));
    }

    [Fact]
    public void Indexer_PresentKey_ReturnsValue_MissingKey_Throws()
    {
        var data = new FormData(new Dictionary<string, string> { ["a"] = "1" });

        Assert.Equal("1", data["a"]);
        Assert.Throws<KeyNotFoundException>(() => data["b"]);
    }

    [Fact]
    public void Surface_ExposesReadOnlyDictionarySemantics()
    {
        var data = new FormData(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.Equal(2, data.Count);
        Assert.True(data.ContainsKey("a"));
        Assert.False(data.ContainsKey("zz"));
        Assert.True(data.TryGetValue("b", out var b) && b == "2");
        Assert.Equal(new[] { "a", "b" }, data.Keys.OrderBy(k => k));
        Assert.Equal(new[] { "1", "2" }, data.Values.OrderBy(v => v));
        Assert.Equal(2, data.Count(_ => true));
    }

    [Fact]
    public void FromJson_ReadsStringNumberBoolNullArray()
    {
        const string json = """
                            {
                              "form": {
                                "name": "alice",
                                "age": 30,
                                "active": true,
                                "verified": false,
                                "nickname": null,
                                "tags": ["x", "y"]
                              }
                            }
                            """;

        using var doc = JsonDocument.Parse(json);
        var data = FormData.FromJson(doc.RootElement);

        Assert.Equal("alice", data["name"]);
        Assert.Equal("30", data["age"]);
        Assert.Equal("true", data["active"]);
        Assert.Equal("false", data["verified"]);
        Assert.Equal(string.Empty, data["nickname"]);
        Assert.Contains("\"x\"", data["tags"]);
        Assert.Contains("\"y\"", data["tags"]);
    }

    [Fact]
    public void FromJson_NoFormProperty_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"id":"h0"}""");

        var data = FormData.FromJson(doc.RootElement);

        Assert.Empty(data);
    }

    [Fact]
    public void FromJson_FormNotObject_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"form": "not-an-object"}""");

        var data = FormData.FromJson(doc.RootElement);

        Assert.Empty(data);
    }

    [Fact]
    public void FromJson_RootNotObject_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("[]");

        var data = FormData.FromJson(doc.RootElement);

        Assert.Empty(data);
    }
}
