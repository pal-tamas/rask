using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class QueryCollectionTests
{
    [Fact]
    public void The_empty_collection_has_a_zero_count() => Assert.Equal(0, QueryCollection.Empty.Count);

    [Fact]
    public void The_empty_collection_answers_any_key_with_empty_values()
    {
        var v = QueryCollection.Empty["missing"];

        Assert.Equal(0, v.Count);
        Assert.Equal(StringValues.Empty, v);
    }

    [Fact]
    public void The_indexer_is_case_insensitive_when_the_store_ignores_case()
    {
        var dict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["Foo"] = "bar" };
        var qc = new QueryCollection(dict);

        Assert.Equal("bar", qc["foo"].ToString());
        Assert.Equal("bar", qc["FOO"].ToString());
    }

    [Fact]
    public void ContainsKey_and_TryGetValue_are_case_insensitive_when_the_store_ignores_case()
    {
        var qc = new QueryCollection(
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["A"] = "1" });

        Assert.True(qc.ContainsKey("a"));
        Assert.True(qc.TryGetValue("a", out var v));
        Assert.Equal("1", v.ToString());
        Assert.False(qc.ContainsKey("missing"));
    }

    [Fact]
    public void A_missing_key_returns_empty_values()
    {
        var qc = new QueryCollection(
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["A"] = "1" });

        Assert.Equal(StringValues.Empty, qc["missing"]);
    }

    [Fact]
    public void Building_from_an_IDictionary_copies_the_entries_and_ignores_case()
    {
        IDictionary<string, StringValues> source =
            new Dictionary<string, StringValues>(StringComparer.Ordinal) { ["A"] = "1", ["b"] = "2" };

        var qc = new QueryCollection(source);

        Assert.Equal(2, qc.Count);
        Assert.True(qc.ContainsKey("a"));
        Assert.True(qc.ContainsKey("B"));
    }

    [Fact]
    public void Enumerating_yields_all_pairs()
    {
        var qc = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "1",
            ["b"] = "2"
        });

        var keys = qc.Select(kv => kv.Key).OrderBy(k => k).ToArray();

        Assert.Equal(new[] { "a", "b" }, keys);
    }

    [Fact]
    public void A_null_store_throws() =>
        Assert.Throws<ArgumentNullException>(() => new QueryCollection(null!));

    [Fact]
    public void The_default_constructor_starts_empty() => Assert.Equal(0, new QueryCollection().Count);
}
