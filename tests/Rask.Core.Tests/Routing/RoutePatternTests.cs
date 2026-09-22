using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RoutePatternTests
{
    [Fact]
    public void The_root_pattern_matches_the_root_path()
    {
        var p = ParseInternal("/");

        Assert.True(TryMatch(p, "/", out var values));
        Assert.Empty(values);
    }

    [Fact]
    public void A_literal_matches_its_exact_path()
    {
        var p = ParseInternal("/dashboard");

        Assert.True(TryMatch(p, "/dashboard", out _));
        Assert.True(TryMatch(p, "/Dashboard", out _));
        Assert.False(TryMatch(p, "/users", out _));
        Assert.False(TryMatch(p, "/dashboard/extra", out _));
    }

    [Fact]
    public void A_parameter_captures_its_segment()
    {
        var p = ParseInternal("/users/{id}");

        Assert.True(TryMatch(p, "/users/42", out var values));
        Assert.Equal("42", values["id"]);
        Assert.False(TryMatch(p, "/users", out _));
        Assert.False(TryMatch(p, "/users/42/extra", out _));
    }

    [Fact]
    public void A_type_constraint_is_stripped_from_the_capture_key()
    {
        // {id:guid} is a generator-side type hint — the runtime doesn't enforce the
        // constraint, but it MUST strip ":guid" off the captured key. Otherwise
        // PageBinder.Bind looks up "Id" in the values dictionary, finds nothing under
        // "id:guid", and the property stays at its default — silently breaking routes
        // like /todos/{id:guid}/edit that look correct on the page but don't bind.
        var p = ParseInternal("/todos/{id:guid}/edit");

        Assert.True(TryMatch(p, "/todos/3e18ad13-d95e-4808-97bd-918b457c006b/edit", out var values));
        Assert.Equal("3e18ad13-d95e-4808-97bd-918b457c006b", values["id"]);
        Assert.False(values.ContainsKey("id:guid"));
    }

    [Fact]
    public void A_type_constraint_is_stripped_from_an_optional_capture_key()
    {
        var p = ParseInternal("/items/{count:int?}");

        Assert.True(TryMatch(p, "/items/42", out var withValue));
        Assert.Equal("42", withValue["count"]);

        Assert.True(TryMatch(p, "/items", out var without));
        Assert.Null(without["count"]);
    }

    [Fact]
    public void An_optional_parameter_may_be_absent()
    {
        var p = ParseInternal("/counter/{name?}");

        Assert.True(TryMatch(p, "/counter/alice", out var withName));
        Assert.Equal("alice", withName["name"]);

        Assert.True(TryMatch(p, "/counter", out var without));
        Assert.Null(without["name"]);
    }

    [Fact]
    public void An_optional_parameter_does_not_match_extra_segments()
    {
        var p = ParseInternal("/counter/{name?}");

        Assert.False(TryMatch(p, "/counter/a/b", out _));
    }

    [Fact]
    public void A_catch_all_greedily_consumes_the_remaining_segments()
    {
        var p = ParseInternal("/files/{**path}");

        Assert.True(TryMatch(p, "/files/a/b/c.txt", out var values));
        Assert.Equal("a/b/c.txt", values["path"]);

        Assert.True(TryMatch(p, "/files", out var empty));
        Assert.Null(empty["path"]);
    }

    [Fact]
    public void A_parameter_decodes_a_percent_encoded_segment()
    {
        var p = ParseInternal("/items/{name}");

        Assert.True(TryMatch(p, "/items/a%20b", out var values));
        Assert.Equal("a b", values["name"]);
    }

    [Fact]
    public void Mixed_literal_and_optional_segments_match_together()
    {
        var p = ParseInternal("/dashboard/settings/{tab?}");

        Assert.True(TryMatch(p, "/dashboard/settings/billing", out var values));
        Assert.Equal("billing", values["tab"]);

        Assert.True(TryMatch(p, "/dashboard/settings", out var withoutTab));
        Assert.Null(withoutTab["tab"]);

        Assert.False(TryMatch(p, "/dashboard/overview", out _));
    }

    [Fact]
    public void An_empty_pattern_is_treated_as_the_root()
    {
        var p = ParseInternal("");

        Assert.True(TryMatch(p, "/", out _));
    }

    [Fact]
    public void Leading_and_trailing_slashes_are_trimmed()
    {
        var p = ParseInternal("dashboard/overview/");

        Assert.True(TryMatch(p, "/dashboard/overview", out _));
        Assert.True(TryMatch(p, "dashboard/overview", out _));
    }

    // RoutePattern is internal; use reflection through a small helper.
    private static object ParseInternal(string template)
    {
        var t = typeof(QueryString).Assembly.GetType("Rask.Core.Routing.RoutePattern", true)!;
        var parse = t.GetMethod("Parse")!;
        return parse.Invoke(null, new object[] { template })!;
    }

    private static bool TryMatch(object pattern, string path, out IDictionary<string, string?> values)
    {
        var t = pattern.GetType();
        var method = t.GetMethod("TryMatch")!;
        object?[] args = { path, null };
        var ok = (bool)method.Invoke(pattern, args)!;
        values = (IDictionary<string, string?>)args[1]!;
        return ok;
    }
}
