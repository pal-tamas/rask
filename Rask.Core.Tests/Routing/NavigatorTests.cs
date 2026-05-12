using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class NavigatorTests
{
    private static (Navigator nav, RouteState state) Build(string path = "/",
        IDictionary<string, StringValues>? query = null)
    {
        var state = new RouteState
        {
            Path = path,
            Query = query is null
                ? QueryCollection.Empty
                : new QueryCollection(new Dictionary<string, StringValues>(query))
        };
        return (new Navigator(state), state);
    }

    [Fact]
    public void Navigate_OutsideHandler_Throws()
    {
        var (nav, _) = Build();
        Assert.Throws<InvalidOperationException>(() => nav.Navigate("/x"));
    }

    [Fact]
    public void SetQuery_OutsideHandler_Throws()
    {
        var (nav, _) = Build();
        Assert.Throws<InvalidOperationException>(() => nav.SetQuery("k", "v"));
    }

    [Fact]
    public void RemoveQuery_OutsideHandler_Throws()
    {
        var (nav, _) = Build();
        Assert.Throws<InvalidOperationException>(() => nav.RemoveQuery("k"));
    }

    [Fact]
    public void ClearQuery_OutsideHandler_Throws()
    {
        var (nav, _) = Build();
        Assert.Throws<InvalidOperationException>(() => nav.ClearQuery());
    }

    [Fact]
    public void Navigate_PathOnly_ClearsExistingQuery()
    {
        var (nav, state) = Build("/old", new Dictionary<string, StringValues> { ["b"] = "2" });
        using (nav.EnterHandler())
        {
            nav.Navigate("/x");
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal(0, state.Query.Count);
    }

    [Fact]
    public void Navigate_PathOnly_DrainsAsPushWithBareUrl()
    {
        var (nav, _) = Build("/old", new Dictionary<string, StringValues> { ["b"] = "2" });
        using (nav.EnterHandler())
        {
            nav.Navigate("/x");
        }

        Assert.True(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal("/x", url);
        Assert.False(replace);
    }

    [Fact]
    public void Navigate_WithQuery_BuildsUrlAndQueryCollection()
    {
        var (nav, state) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/x",
                new[]
                {
                    KeyValuePair.Create<string, string?>("a", "1"), KeyValuePair.Create<string, string?>("b", "2")
                });
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal("1", state.Query["a"].ToString());
        Assert.Equal("2", state.Query["b"].ToString());
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/x?a=1&b=2", url);
    }

    [Fact]
    public void SetQuery_NullValue_RemovesKey()
    {
        var (nav, state) = Build("/p");
        using (nav.EnterHandler())
        {
            nav.SetQuery("a", "1");
            nav.SetQuery("a", null);
        }

        Assert.False(state.Query.ContainsKey("a"));
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/p", url);
    }

    [Fact]
    public void SetQuery_Multiple_AddsAndUpdates()
    {
        var (nav, state) = Build("/p", new Dictionary<string, StringValues> { ["a"] = "1" });
        using (nav.EnterHandler())
        {
            nav.SetQuery(
                KeyValuePair.Create<string, string?>("a", "9"),
                KeyValuePair.Create<string, string?>("b", "2"));
        }

        Assert.Equal("9", state.Query["a"].ToString());
        Assert.Equal("2", state.Query["b"].ToString());
    }

    [Fact]
    public void RemoveQuery_DropsKeyAndDirties()
    {
        var (nav, state) = Build("/p", new Dictionary<string, StringValues> { ["a"] = "1", ["b"] = "2" });
        using (nav.EnterHandler())
        {
            nav.RemoveQuery("a");
        }

        Assert.False(state.Query.ContainsKey("a"));
        Assert.True(state.Query.ContainsKey("b"));
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/p?b=2", url);
    }

    [Fact]
    public void ClearQuery_EmptiesQuery()
    {
        var (nav, state) = Build("/p", new Dictionary<string, StringValues> { ["a"] = "1", ["b"] = "2" });
        using (nav.EnterHandler())
        {
            nav.ClearQuery();
        }

        Assert.Equal(0, state.Query.Count);
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/p", url);
    }

    [Fact]
    public void Replace_StickyAcrossLaterQueryMutations()
    {
        var (nav, _) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/x", true);
            nav.SetQuery("k", "v");
        }

        Assert.True(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal("/x?k=v", url);
        Assert.True(replace);
    }

    [Fact]
    public void Navigate_DefaultPushOverridesPriorReplace()
    {
        var (nav, _) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/x", true);
            nav.Navigate("/y");
        }

        Assert.True(nav.TryConsumeHistory(out _, out var replace));
        Assert.False(replace);
    }

    [Fact]
    public void TryConsumeHistory_WhenNotDirty_ReturnsFalse()
    {
        var (nav, _) = Build();
        Assert.False(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal(string.Empty, url);
        Assert.False(replace);
    }

    [Fact]
    public void TryConsumeHistory_AfterDrain_ReturnsFalseUntilNextChange()
    {
        var (nav, _) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/x");
        }

        Assert.True(nav.TryConsumeHistory(out _, out _));
        Assert.False(nav.TryConsumeHistory(out _, out _));
    }

    [Fact]
    public void EnterHandler_DisposeRestoresGuard()
    {
        var (nav, _) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/x");
        }

        Assert.Throws<InvalidOperationException>(() => nav.Navigate("/y"));
    }

    [Fact]
    public void BuildUrl_EncodesValues()
    {
        var (nav, _) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/p", new[] { KeyValuePair.Create<string, string?>("q", "a b&c") });
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Contains("q=a%20b%26c", url);
    }

    [Fact]
    public void Navigate_RouteUrlPathOnly_ClearsQueryAndSetsPath()
    {
        var (nav, state) = Build("/old", new Dictionary<string, StringValues> { ["b"] = "2" });
        using (nav.EnterHandler())
        {
            nav.Navigate(new RouteUrl("/x"));
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal(0, state.Query.Count);
    }

    [Fact]
    public void Navigate_RouteUrlWithQueryString_ParsesIntoQueryCollection()
    {
        var (nav, state) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate(new RouteUrl("/x", "?a=1&b=2"));
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal("1", state.Query["a"].ToString());
        Assert.Equal("2", state.Query["b"].ToString());
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/x?a=1&b=2", url);
    }

    [Fact]
    public void Navigate_RouteUrl_ReplaceFlagPropagates()
    {
        var (nav, _) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate(new RouteUrl("/x"), true);
        }

        Assert.True(nav.TryConsumeHistory(out _, out var replace));
        Assert.True(replace);
    }

    [Fact]
    public void Navigate_StringOverload_StillWorks()
    {
        var (nav, state) = Build();
        using (nav.EnterHandler())
        {
            nav.Navigate("/x");
        }

        Assert.Equal("/x", state.Path);
    }
}
