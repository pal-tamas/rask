using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// `[QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];` reads `?drivers=ada,theo`: one key,
// its elements split on commas, each read the way a single value is, and what cannot be read left out.
public partial class QueryParamListTests : global::Rask.Core.RaskMarkup
{
    private static readonly Dictionary<string, string?> NoSegments = new(StringComparer.Ordinal);

    private static QueryCollection Query(params (string key, string value)[] pairs) =>
        new(pairs.ToDictionary(p => p.key, p => new StringValues(p.value), StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void A_list_of_strings_reads_each_comma_separated_element()
    {
        var page = new FleetPage();

        var changed = PageBinder.Bind(page, NoSegments, Query(("drivers", "ada,theo")));

        Assert.True(changed);
        Assert.Equal(["ada", "theo"], page.Drivers);
    }

    [Fact]
    public void A_list_of_numbers_reads_each_element_as_a_number()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("tanks", "3,1,2")));

        Assert.Equal([3, 1, 2], page.Tanks);
    }

    [Fact]
    public void A_list_of_guids_reads_each_element_as_a_guid()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("ids", $"{first},{second}")));

        Assert.Equal([first, second], page.Ids);
    }

    [Fact]
    public void A_list_of_flags_reads_one_and_zero_as_a_single_flag_does()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("flags", "1,false,0,TRUE")));

        Assert.Equal([true, false, false, true], page.Flags);
    }

    [Fact]
    public void An_unreadable_element_is_left_out_and_the_rest_are_read()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("tanks", "3,abc,2,1.5")));

        Assert.Equal([3, 2], page.Tanks);
    }

    [Theory]
    [InlineData("a,,b")]
    [InlineData(",a,b,")]
    [InlineData("a,b,,,")]
    public void Empty_elements_are_left_out(string text)
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("drivers", text)));

        Assert.Equal(["a", "b"], page.Drivers);
    }

    [Fact]
    public void An_encoded_comma_stays_inside_its_element()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("drivers", "Lovelace%2C Ada,theo,100%25")));

        Assert.Equal(["Lovelace, Ada", "theo", "100%"], page.Drivers);
    }

    [Fact]
    public void The_same_key_given_twice_reads_the_elements_of_both()
    {
        var page = new FleetPage();
        var repeated = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["drivers"] = new(["ada", "theo,max"]),
        });

        PageBinder.Bind(page, NoSegments, repeated);

        Assert.Equal(["ada", "theo", "max"], page.Drivers);
    }

    [Fact]
    public void An_absent_key_keeps_the_initializer()
    {
        var page = new FleetPage();
        var built = page.Tanks;

        var changed = PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.False(changed);
        Assert.Same(built, page.Tanks);
        Assert.Equal([7], page.Tanks);
    }

    [Theory]
    [InlineData("abc,x")]
    [InlineData(",,")]
    [InlineData("")]
    public void A_key_with_no_readable_element_keeps_the_initializer(string text)
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, Query(("tanks", text)));

        Assert.Equal([7], page.Tanks);
    }

    [Fact]
    public void Going_back_to_an_address_without_the_key_restores_the_initializer()
    {
        var page = new FleetPage();
        PageBinder.Bind(page, NoSegments, Query(("drivers", "ada,theo")));

        var changed = PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.True(changed);
        Assert.Empty(page.Drivers);
    }

    [Fact]
    public void The_same_elements_bound_again_are_no_change_and_keep_the_list()
    {
        var page = new FleetPage();
        PageBinder.Bind(page, NoSegments, Query(("drivers", "ada,theo")));
        var first = page.Drivers;

        var changed = PageBinder.Bind(page, NoSegments, Query(("drivers", "ada,theo")));

        Assert.False(changed);
        Assert.Same(first, page.Drivers);
    }

    [Fact]
    public void Other_elements_bound_again_are_a_change()
    {
        var page = new FleetPage();
        PageBinder.Bind(page, NoSegments, Query(("drivers", "ada,theo")));

        var changed = PageBinder.Bind(page, NoSegments, Query(("drivers", "ada")));

        Assert.True(changed);
        Assert.Equal(["ada"], page.Drivers);
    }

    [Fact]
    public void An_array_and_an_enumerable_bind_as_a_read_only_list_does()
    {
        var page = new ShapesPage();

        PageBinder.Bind(page, NoSegments, Query(("days", "2026-10-21,nope,2026-10-22"), ("names", "a,b"), ("sizes", "1,2")));

        Assert.Equal([new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22)], page.Days);
        Assert.Equal(["a", "b"], page.Names);
        Assert.Equal([1L, 2L], page.Sizes);
    }

    [SkipFactory]
    private sealed class FleetPage : Component
    {
        [QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];
        [QueryParam] public IReadOnlyList<int> Tanks { get; set; } = [7];
        [QueryParam] public IReadOnlyList<Guid> Ids { get; set; } = [];
        [QueryParam] public IReadOnlyList<bool> Flags { get; set; } = [];
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class ShapesPage : Component
    {
        [QueryParam] public DateOnly[] Days { get; set; } = [];
        [QueryParam] public IEnumerable<string>? Names { get; set; }
        [QueryParam] public IReadOnlyCollection<long> Sizes { get; set; } = [];
        protected override Component? Render() => Span;
    }
}
