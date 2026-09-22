using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class QueryStringTests
{
    [Fact]
    public void An_empty_query_parses_to_nothing()
    {
        Assert.Equal(0, QueryString.Parse(null).Count);
        Assert.Equal(0, QueryString.Parse("").Count);
        Assert.Equal(0, QueryString.Parse("?").Count);
    }

    [Fact]
    public void The_leading_question_mark_is_optional()
    {
        var withMark = QueryString.Parse("?a=1&b=2");
        var withoutMark = QueryString.Parse("a=1&b=2");

        Assert.Equal(2, withMark.Count);
        Assert.Equal(2, withoutMark.Count);
        Assert.Equal("1", withMark["a"].ToString());
        Assert.Equal("1", withoutMark["a"].ToString());
    }

    [Fact]
    public void A_missing_value_parses_as_an_empty_string()
    {
        var q = QueryString.Parse("?flag");

        Assert.True(q.ContainsKey("flag"));
        Assert.Equal(string.Empty, q["flag"].ToString());
    }

    [Fact]
    public void Percent_encoded_values_are_decoded()
    {
        var q = QueryString.Parse("?q=a%20b%26c&where=%2Fx%2Fy");

        Assert.Equal("a b&c", q["q"].ToString());
        Assert.Equal("/x/y", q["where"].ToString());
    }

    [Fact]
    public void A_plus_parses_as_a_space()
    {
        var q = QueryString.Parse("?name=jane+doe");

        Assert.Equal("jane doe", q["name"].ToString());
    }

    [Fact]
    public void Repeated_keys_accumulate_into_one_StringValues()
    {
        var q = QueryString.Parse("?tag=a&tag=b&tag=c");

        Assert.Equal(1, q.Count);
        Assert.Equal(new[] { "a", "b", "c" }, q["tag"].ToArray());
    }

    [Fact]
    public void Parsed_keys_are_case_insensitive()
    {
        var q = QueryString.Parse("?Name=alice");

        Assert.Equal("alice", q["name"].ToString());
        Assert.Equal("alice", q["NAME"].ToString());
        Assert.True(q.ContainsKey("nAmE"));
    }

    [Fact]
    public void Stray_and_trailing_ampersands_are_skipped()
    {
        var q = QueryString.Parse("?a=1&&b=2&");

        Assert.Equal(2, q.Count);
        Assert.Equal("1", q["a"].ToString());
        Assert.Equal("2", q["b"].ToString());
    }

    [Fact]
    public void An_empty_query_builds_the_bare_path() => Assert.Equal("/users",
        QueryString.Build("/users", Array.Empty<KeyValuePair<string, StringValues>>()));

    [Fact]
    public void A_single_parameter_adds_the_question_mark()
    {
        var pairs = new[] { new KeyValuePair<string, StringValues>("id", "42") };

        Assert.Equal("/users?id=42", QueryString.Build("/users", pairs));
    }

    [Fact]
    public void Multiple_parameters_are_delimited_with_ampersands()
    {
        var pairs = new[]
        {
            new KeyValuePair<string, StringValues>("a", "1"), new KeyValuePair<string, StringValues>("b", "2")
        };

        Assert.Equal("/p?a=1&b=2", QueryString.Build("/p", pairs));
    }

    [Fact]
    public void A_repeated_key_repeats_in_the_output()
    {
        var pairs = new[] { new KeyValuePair<string, StringValues>("tag", new StringValues(new[] { "a", "b" })) };

        Assert.Equal("/p?tag=a&tag=b", QueryString.Build("/p", pairs));
    }

    [Fact]
    public void Built_values_are_encoded()
    {
        var pairs = new[] { new KeyValuePair<string, StringValues>("q", "a b&c") };

        Assert.Equal("/p?q=a%20b%26c", QueryString.Build("/p", pairs));
    }

    [Fact]
    public void A_parse_then_build_round_trip_preserves_the_query()
    {
        var input = "?a=1&b=hello%20world&tag=x&tag=y";
        var parsed = QueryString.Parse(input);
        var pairs = new List<KeyValuePair<string, StringValues>>(parsed.Count);
        foreach (var kv in parsed)
        {
            pairs.Add(kv);
        }

        var rebuilt = QueryString.Build("", pairs);

        Assert.Equal("?a=1&b=hello%20world&tag=x&tag=y", rebuilt);
    }
}
