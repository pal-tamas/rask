using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class QueryCollectionTests
{
    [Fact]
    public void Empty_HasZeroCount() => Assert.Equal(0, QueryCollection.Empty.Count);

    [Fact]
    public void Empty_Indexer_ReturnsStringValuesEmpty()
    {
        var v = QueryCollection.Empty["missing"];

        Assert.Equal(0, v.Count);
        Assert.Equal(StringValues.Empty, v);
    }

    [Fact]
    public void Indexer_IsCaseInsensitive_WhenStoreUsesIgnoreCase()
    {
        var dict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["Foo"] = "bar" };
        var qc = new QueryCollection(dict);

        Assert.Equal("bar", qc["foo"].ToString());
        Assert.Equal("bar", qc["FOO"].ToString());
    }

    [Fact]
    public void ContainsKey_TryGetValue_AreCaseInsensitive_WhenStoreUsesIgnoreCase()
    {
        var qc = new QueryCollection(
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["A"] = "1" });

        Assert.True(qc.ContainsKey("a"));
        Assert.True(qc.TryGetValue("a", out var v));
        Assert.Equal("1", v.ToString());
        Assert.False(qc.ContainsKey("missing"));
    }

    [Fact]
    public void Indexer_MissingKey_ReturnsStringValuesEmpty()
    {
        var qc = new QueryCollection(
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["A"] = "1" });

        Assert.Equal(StringValues.Empty, qc["missing"]);
    }

    [Fact]
    public void Constructor_FromIDictionary_CopiesEntriesAndIsCaseInsensitive()
    {
        IDictionary<string, StringValues> source =
            new Dictionary<string, StringValues>(StringComparer.Ordinal) { ["A"] = "1", ["b"] = "2" };

        var qc = new QueryCollection(source);

        Assert.Equal(2, qc.Count);
        Assert.True(qc.ContainsKey("a"));
        Assert.True(qc.ContainsKey("B"));
    }

    [Fact]
    public void Enumerator_YieldsAllPairs()
    {
        var qc = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "1", ["b"] = "2"
        });

        var keys = qc.Select(kv => kv.Key).OrderBy(k => k).ToArray();

        Assert.Equal(new[] { "a", "b" }, keys);
    }

    [Fact]
    public void Constructor_NullStore_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new QueryCollection(null!));

    [Fact]
    public void Default_Constructor_StartsEmpty() => Assert.Equal(0, new QueryCollection().Count);
}
