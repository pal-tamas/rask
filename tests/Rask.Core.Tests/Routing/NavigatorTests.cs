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
    public void Navigating_outside_a_handler_throws()
    {
        var (nav, _) = Build();

        Assert.Throws<InvalidOperationException>(() => nav.NavigateTo("/x"));
    }

    [Fact]
    public void Setting_the_query_outside_a_handler_throws()
    {
        var (nav, _) = Build();

        Assert.Throws<InvalidOperationException>(() => nav.SetQuery("k", "v"));
    }

    [Fact]
    public void Removing_a_query_key_outside_a_handler_throws()
    {
        var (nav, _) = Build();

        Assert.Throws<InvalidOperationException>(() => nav.RemoveQuery("k"));
    }

    [Fact]
    public void Clearing_the_query_outside_a_handler_throws()
    {
        var (nav, _) = Build();

        Assert.Throws<InvalidOperationException>(() => nav.ClearQuery());
    }

    [Fact]
    public void An_unconsumed_navigation_does_not_leak_into_the_next_handler()
    {
        // A handler that queues a navigation but never consumes it (e.g. it threw before
        // TryConsumeHistory ran) must not leak that pending nav — including the replace flag —
        // into the next dispatch and fire a navigation the user never triggered there.
        var (nav, _) = Build("/start");

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/a", true);
        } // scope disposed WITHOUT TryConsumeHistory — simulates a faulted handler

        using (nav.EnterHandler())
        {
            Assert.False(nav.TryConsumeHistory(out _, out _));
        }
    }

    [Fact]
    public void Navigating_to_a_bare_path_clears_the_existing_query()
    {
        var (nav, state) = Build("/old", new Dictionary<string, StringValues> { ["b"] = "2" });

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x");
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal(0, state.Query.Count);
    }

    [Fact]
    public void Navigating_to_a_bare_path_drains_as_a_push_with_the_bare_url()
    {
        var (nav, _) = Build("/old", new Dictionary<string, StringValues> { ["b"] = "2" });

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x");
        }

        Assert.True(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal("/x", url);
        Assert.False(replace);
    }

    [Fact]
    public void Navigating_with_a_query_builds_the_url_and_the_query_collection()
    {
        var (nav, state) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x",
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
    public void Setting_a_query_key_to_null_removes_it()
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
    public void Setting_several_query_keys_adds_and_updates_them()
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
    public void Removing_a_query_key_drops_it_and_dirties_the_history()
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
    public void Clearing_the_query_empties_it()
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
    public void Replace_stays_sticky_across_later_query_mutations()
    {
        var (nav, _) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x", true);
            nav.SetQuery("k", "v");
        }

        Assert.True(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal("/x?k=v", url);
        Assert.True(replace);
    }

    [Fact]
    public void A_default_push_overrides_a_prior_replace()
    {
        var (nav, _) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x", true);
            nav.NavigateTo("/y");
        }

        Assert.True(nav.TryConsumeHistory(out _, out var replace));
        Assert.False(replace);
    }

    [Fact]
    public void Consuming_history_when_nothing_changed_returns_false()
    {
        var (nav, _) = Build();

        Assert.False(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal(string.Empty, url);
        Assert.False(replace);
    }

    [Fact]
    public void Consuming_history_after_a_drain_returns_false_until_the_next_change()
    {
        var (nav, _) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x");
        }

        Assert.True(nav.TryConsumeHistory(out _, out _));
        Assert.False(nav.TryConsumeHistory(out _, out _));
    }

    [Fact]
    public void Disposing_the_handler_scope_restores_the_guard()
    {
        var (nav, _) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x");
        }

        Assert.Throws<InvalidOperationException>(() => nav.NavigateTo("/y"));
    }

    [Fact]
    public void The_built_url_encodes_its_values()
    {
        var (nav, _) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/p", new[] { KeyValuePair.Create<string, string?>("q", "a b&c") });
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Contains("q=a%20b%26c", url);
    }

    [Fact]
    public void Navigating_to_a_bare_RouteUrl_clears_the_query_and_sets_the_path()
    {
        var (nav, state) = Build("/old", new Dictionary<string, StringValues> { ["b"] = "2" });

        using (nav.EnterHandler())
        {
            nav.NavigateTo(new RouteUrl("/x"));
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal(0, state.Query.Count);
    }

    [Fact]
    public void Navigating_to_a_RouteUrl_with_a_query_string_parses_it_into_the_query_collection()
    {
        var (nav, state) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo(new RouteUrl("/x", "?a=1&b=2"));
        }

        Assert.Equal("/x", state.Path);
        Assert.Equal("1", state.Query["a"].ToString());
        Assert.Equal("2", state.Query["b"].ToString());
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/x?a=1&b=2", url);
    }

    [Fact]
    public void The_replace_flag_propagates_when_navigating_to_a_RouteUrl()
    {
        var (nav, _) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo(new RouteUrl("/x"), true);
        }

        Assert.True(nav.TryConsumeHistory(out _, out var replace));
        Assert.True(replace);
    }

    [Fact]
    public void Navigating_through_the_string_overload_still_works()
    {
        var (nav, state) = Build();

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/x");
        }

        Assert.Equal("/x", state.Path);
    }
}
