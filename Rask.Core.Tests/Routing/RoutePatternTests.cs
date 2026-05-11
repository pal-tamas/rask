using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RoutePatternTests
{
    [Fact]
    public void Root_Matches_RootPath()
    {
        var p = ParseInternal("/");
        Assert.True(TryMatch(p, "/", out var values));
        Assert.Empty(values);
    }

    [Fact]
    public void Literal_Matches_ExactPath()
    {
        var p = ParseInternal("/dashboard");
        Assert.True(TryMatch(p, "/dashboard", out _));
        Assert.True(TryMatch(p, "/Dashboard", out _));
        Assert.False(TryMatch(p, "/users", out _));
        Assert.False(TryMatch(p, "/dashboard/extra", out _));
    }

    [Fact]
    public void Parameter_CapturesSegment()
    {
        var p = ParseInternal("/users/{id}");
        Assert.True(TryMatch(p, "/users/42", out var values));
        Assert.Equal("42", values["id"]);
        Assert.False(TryMatch(p, "/users", out _));
        Assert.False(TryMatch(p, "/users/42/extra", out _));
    }

    [Fact]
    public void OptionalParameter_AllowsAbsence()
    {
        var p = ParseInternal("/counter/{name?}");
        Assert.True(TryMatch(p, "/counter/alice", out var withName));
        Assert.Equal("alice", withName["name"]);

        Assert.True(TryMatch(p, "/counter", out var without));
        Assert.Null(without["name"]);
    }

    [Fact]
    public void OptionalParameter_DoesNotMatchExtraSegments()
    {
        var p = ParseInternal("/counter/{name?}");
        Assert.False(TryMatch(p, "/counter/a/b", out _));
    }

    [Fact]
    public void CatchAll_GreedilyConsumesRemaining()
    {
        var p = ParseInternal("/files/{**path}");
        Assert.True(TryMatch(p, "/files/a/b/c.txt", out var values));
        Assert.Equal("a/b/c.txt", values["path"]);

        Assert.True(TryMatch(p, "/files", out var empty));
        Assert.Null(empty["path"]);
    }

    [Fact]
    public void Parameter_DecodesPercentEncodedSegment()
    {
        var p = ParseInternal("/items/{name}");
        Assert.True(TryMatch(p, "/items/a%20b", out var values));
        Assert.Equal("a b", values["name"]);
    }

    [Fact]
    public void Multiple_Segments_Mixed()
    {
        var p = ParseInternal("/dashboard/settings/{tab?}");
        Assert.True(TryMatch(p, "/dashboard/settings/billing", out var values));
        Assert.Equal("billing", values["tab"]);

        Assert.True(TryMatch(p, "/dashboard/settings", out var withoutTab));
        Assert.Null(withoutTab["tab"]);

        Assert.False(TryMatch(p, "/dashboard/overview", out _));
    }

    [Fact]
    public void EmptyPattern_TreatedAsRoot()
    {
        var p = ParseInternal("");
        Assert.True(TryMatch(p, "/", out _));
    }

    [Fact]
    public void TrimsLeadingAndTrailingSlashes()
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
