using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclass has no generated factory

namespace Rask.Core.Tests.Routing;

// A list is written as ONE value with its elements joined by commas, in the order given, and the whole of it
// encoded — the commas as %2C; a list with nothing to write is written as nothing, which leaves its key out.
public class QueryListWritingTests
{
    [Fact]
    public void A_list_is_written_with_its_elements_joined_by_encoded_commas_in_order()
    {
        IReadOnlyList<string> drivers = ["theo", "ada", "a b"];

        var text = RouteValueFormatter.Format(drivers);

        Assert.Equal("theo%2Cada%2Ca%20b", text);
    }

    [Fact]
    public void A_written_list_reads_back_as_the_same_elements()
    {
        IReadOnlyList<string> drivers = ["ada", "100%", "x&y=z", "árvíz"];

        var text = RouteValueFormatter.Format(drivers);

        Assert.Equal(drivers, ReadBack(text));
    }

    [Fact]
    public void An_element_written_with_a_comma_reads_back_as_two_elements()
    {
        IReadOnlyList<string> drivers = ["Lovelace, Ada", "theo"];

        var text = RouteValueFormatter.Format(drivers);

        Assert.Equal("Lovelace%2C%20Ada%2Ctheo", text);
        Assert.Equal(["Lovelace", " Ada", "theo"], ReadBack(text));
    }

    [Fact]
    public void Numbers_and_flags_are_written_with_the_invariant_culture()
    {
        IReadOnlyList<double> ratios = [0.5, 2];
        bool[] flags = [true, false];

        var written = (RouteValueFormatter.Format(ratios), RouteValueFormatter.Format(flags));

        Assert.Equal(("0.5%2C2", "true%2Cfalse"), written);
    }

    [Fact]
    public void Empty_and_null_elements_are_not_written()
    {
        IReadOnlyList<string?> drivers = ["", null, "ada", ""];

        var text = RouteValueFormatter.Format(drivers);

        Assert.Equal("ada", text);
    }

    [Fact]
    public void An_empty_list_writes_nothing()
    {
        IReadOnlyList<int> none = [];

        var text = RouteValueFormatter.Format(none);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void A_string_is_still_one_value_and_not_a_list_of_characters()
    {
        const string text = "a,b";

        var written = RouteValueFormatter.Format(text);

        Assert.Equal("a%2Cb", written);
    }

    [Fact]
    public void Go_With_a_comma_joined_value_is_read_back_as_the_list()
    {
        string[] drivers = ["ada", "theo"];
        var nav = new Navigator(new RouteState { Path = "/fleet" });

        using (nav.EnterHandler())
        {
            Go.With("drivers", string.Join(',', drivers));
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/fleet?drivers=ada%2Ctheo", url);
        Assert.Equal(drivers, ReadBack(url["/fleet?drivers=".Length..]));
    }

    // Through the same reader an address goes through: parsed, then bound.
    private static IReadOnlyList<string> ReadBack(string written)
    {
        var page = new ReadBackPage();
        PageBinder.Bind(page, new Dictionary<string, string?>(), QueryString.Parse("?drivers=" + written));
        return page.Drivers;
    }

    [SkipFactory]
    private sealed class ReadBackPage : Component
    {
        [QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];
        protected override Component? Render() => null;
    }
}
