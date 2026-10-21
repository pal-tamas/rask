using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// A list is written as ONE key with its elements joined by commas, in the order given; an element's own comma
// is encoded so it reads back as one element, and a list with nothing to write writes no key.
public class QueryListWritingTests
{
    [Fact]
    public void A_list_is_written_with_its_elements_joined_by_commas_in_order()
    {
        IReadOnlyList<string> drivers = ["theo", "ada"];

        var text = RouteValueFormatter.FormatList(drivers);

        Assert.Equal("theo,ada", text);
    }

    [Fact]
    public void An_element_with_a_comma_is_encoded_so_it_reads_back_as_one_element()
    {
        IReadOnlyList<string> drivers = ["Lovelace, Ada", "100%", "a b"];

        var text = RouteValueFormatter.FormatList(drivers);

        Assert.Equal("Lovelace%252C%20Ada,100%2525,a%20b", text);
        Assert.Equal(["Lovelace, Ada", "100%", "a b"], ReadBack("drivers", text));
    }

    [Fact]
    public void Numbers_and_flags_are_written_with_the_invariant_culture()
    {
        IReadOnlyList<double> ratios = [0.5, 2];
        IReadOnlyList<bool> flags = [true, false];

        var written = (RouteValueFormatter.FormatList(ratios), RouteValueFormatter.FormatList(flags));

        Assert.Equal(("0.5,2", "true,false"), written);
    }

    [Fact]
    public void Empty_and_null_elements_are_not_written()
    {
        IReadOnlyList<string?> drivers = ["", null, "ada", ""];

        var text = RouteValueFormatter.FormatList(drivers);

        Assert.Equal("ada", text);
    }

    [Fact]
    public void An_empty_or_missing_list_writes_nothing()
    {
        IReadOnlyList<int> none = [];

        var written = (RouteValueFormatter.FormatList(none), RouteValueFormatter.FormatList<int>(null));

        Assert.Equal((string.Empty, string.Empty), written);
    }

    [Fact]
    public void Go_With_a_list_sets_one_key_with_the_elements_joined_by_commas()
    {
        var state = new RouteState { Path = "/fleet" };
        var nav = new Navigator(state);

        using (nav.EnterHandler())
        {
            Go.With("drivers", ["ada", "Lovelace, Ada"]);
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?drivers=ada,Lovelace%252C%20Ada", url);
        Assert.Equal(["ada", "Lovelace, Ada"], ReadBack("drivers", url["/fleet?drivers=".Length..]));
    }

    [Fact]
    public void Go_With_an_empty_list_removes_the_key()
    {
        var state = new RouteState
        {
            Path = "/fleet",
            Query = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
            {
                ["drivers"] = "ada",
                ["page"] = "2",
            }),
        };
        var nav = new Navigator(state);

        using (nav.EnterHandler())
        {
            Go.With("drivers", Array.Empty<int>());
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?page=2", url);
    }

    // Through the same reader an address goes through: parsed, then bound.
    private static IReadOnlyList<string> ReadBack(string key, string written)
    {
        var page = new ReadBackPage();
        PageBinder.Bind(page, new Dictionary<string, string?>(), QueryString.Parse("?" + key + "=" + written));
        return page.Drivers;
    }

#pragma warning disable RASK014 // test-defined Component subclass has no generated factory
    [SkipFactory]
    private sealed class ReadBackPage : Component
    {
        [QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];
        protected override Component? Render() => null;
    }
#pragma warning restore RASK014
}
