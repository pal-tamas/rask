using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclass has no generated factory

namespace Rask.Core.Tests.Routing;

// `Go.With("drivers", ["ada", "theo"])` writes a list as Laravel's route() and Rails' to_query do — the key repeated
// with empty brackets, one pair per element, in order — and replaces the key in every form the address had it.
public class QueryListWritingTests
{
    [Fact]
    public void Go_With_a_list_writes_the_key_repeated_with_brackets_in_order()
    {
        string[] drivers = ["theo", "ada", "a b"];
        var nav = new Navigator(new RouteState { Path = "/fleet" });

        using (nav.EnterHandler())
        {
            Go.With("drivers", drivers);
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?drivers%5B%5D=theo&drivers%5B%5D=ada&drivers%5B%5D=a%20b", url);
        Assert.Equal(drivers, ReadBack(url));
    }

    [Fact]
    public void An_element_with_a_comma_is_written_and_read_back_whole()
    {
        string[] drivers = ["Lovelace, Ada", "theo"];
        var nav = new Navigator(new RouteState { Path = "/fleet" });

        using (nav.EnterHandler())
        {
            Go.With("drivers", drivers);
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?drivers%5B%5D=Lovelace%2C%20Ada&drivers%5B%5D=theo", url);
        Assert.Equal(drivers, ReadBack(url));
    }

    [Fact]
    public void Numbers_and_flags_are_written_with_the_invariant_culture()
    {
        var nav = new Navigator(new RouteState { Path = "/fleet" });

        using (nav.EnterHandler())
        {
            Go.With("ratios", [0.5, 2.0]);
            Go.With("flags", [true, false]);
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Contains("ratios%5B%5D=0.5&ratios%5B%5D=2", url, StringComparison.Ordinal);
        Assert.Contains("flags%5B%5D=true&flags%5B%5D=false", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_and_null_elements_are_not_written()
    {
        string?[] drivers = ["", null, "ada", ""];
        var nav = new Navigator(new RouteState { Path = "/fleet" });

        using (nav.EnterHandler())
        {
            Go.With("drivers", drivers);
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?drivers%5B%5D=ada", url);
    }

    [Fact]
    public void Go_With_a_list_replaces_the_key_in_every_form_and_keeps_the_others()
    {
        var nav = new Navigator(new RouteState { Path = "/fleet", Query = Query("drivers", "Drivers[]", "drivers[0]", "page") });

        using (nav.EnterHandler())
        {
            Go.With("DRIVERS", ["ada"]);
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        var keys = QueryString.Parse(url[url.IndexOf('?', StringComparison.Ordinal)..]).Keys;
        Assert.Equal(["DRIVERS[]", "page"], keys.Order(StringComparer.Ordinal));
        Assert.Equal(["ada"], ReadBack(url));
    }

    [Fact]
    public void Go_With_an_empty_list_removes_the_key()
    {
        var nav = new Navigator(new RouteState { Path = "/fleet", Query = Query("drivers[]", "page") });

        using (nav.EnterHandler())
        {
            Go.With("drivers", Array.Empty<int>());
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?page=x", url);
    }

    [Fact]
    public void Go_With_a_string_still_sets_one_value_and_not_a_list_of_characters()
    {
        var nav = new Navigator(new RouteState { Path = "/fleet" });

        using (nav.EnterHandler())
        {
            Go.With("page", "22");
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?page=22", url);
    }

    private static QueryCollection Query(params string[] keys) =>
        new(keys.ToDictionary(key => key, _ => new StringValues("x"), StringComparer.OrdinalIgnoreCase));

    // Through the same reader an address goes through: parsed, then bound.
    private static IReadOnlyList<string> ReadBack(string url)
    {
        var page = new ReadBackPage();
        PageBinder.Bind(page, new Dictionary<string, string?>(), QueryString.Parse(url[url.IndexOf('?', StringComparison.Ordinal)..]));
        return page.Drivers;
    }

    [SkipFactory]
    private sealed class ReadBackPage : Component
    {
        [QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];
        protected override Component? Render() => null;
    }
}
