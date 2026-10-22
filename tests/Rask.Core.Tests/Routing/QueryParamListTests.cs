using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// `[QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];` reads `?drivers[]=ada&drivers[]=theo`,
// as Laravel and Rails do: the key repeated with brackets (or Laravel's `drivers[0]=`), each element read the way a
// single value is, and what cannot be read left out.
public partial class QueryParamListTests : global::Rask.Core.RaskMarkup
{
    private static readonly Dictionary<string, string?> NoSegments = new(StringComparer.Ordinal);

    [Fact]
    public void A_list_of_strings_reads_each_bracketed_pair_in_order()
    {
        var page = new FleetPage();

        var changed = PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=theo&drivers[]=ada"));

        Assert.True(changed);
        Assert.Equal(["theo", "ada"], page.Drivers);
    }

    [Fact]
    public void Encoded_brackets_read_as_brackets()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers%5B%5D=ada&drivers%5B%5D=theo"));

        Assert.Equal(["ada", "theo"], page.Drivers);
    }

    [Fact]
    public void Indexed_pairs_read_in_the_order_of_their_indexes()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[2]=max&drivers[0]=ada&drivers[10]=zoe&drivers[1]=theo"));

        Assert.Equal(["ada", "theo", "max", "zoe"], page.Drivers);
    }

    [Fact]
    public void A_plain_key_reads_as_a_list_of_one()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers=ada"));

        Assert.Equal(["ada"], page.Drivers);
    }

    [Fact]
    public void The_key_matches_without_regard_to_case()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?DRIVERS[]=ada&Drivers[]=theo"));

        Assert.Equal(["ada", "theo"], page.Drivers);
    }

    [Fact]
    public void A_comma_is_part_of_its_element_and_separates_nothing()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=Lovelace%2C%20Ada&drivers[]=a,b"));

        Assert.Equal(["Lovelace, Ada", "a,b"], page.Drivers);
    }

    [Fact]
    public void Another_key_that_starts_with_the_name_is_not_the_list()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?driversX=a&drivers[x]=b&drivers[-1]=c&drivers[]=ada"));

        Assert.Equal(["ada"], page.Drivers);
    }

    [Fact]
    public void A_list_of_numbers_reads_each_element_as_a_number()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?tanks[]=3&tanks[]=1&tanks[]=2"));

        Assert.Equal([3, 1, 2], page.Tanks);
    }

    [Fact]
    public void A_list_of_guids_reads_each_element_as_a_guid()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse($"?ids[]={first}&ids[]={second}"));

        Assert.Equal([first, second], page.Ids);
    }

    [Fact]
    public void A_list_of_flags_reads_one_and_zero_as_a_single_flag_does()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?flags[]=1&flags[]=false&flags[]=0&flags[]=TRUE"));

        Assert.Equal([true, false, false, true], page.Flags);
    }

    [Fact]
    public void A_list_of_any_parsable_type_reads_each_element_with_its_own_parser()
    {
        var page = new ShapesPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?customers[]=C-1&customers[]=x&customers[]=C-42"));

        Assert.Equal([new PageBinderTests.CustomerId(1), new PageBinderTests.CustomerId(42)], page.Customers);
    }

    [Fact]
    public void An_unreadable_element_is_left_out_and_the_rest_are_read()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?tanks[]=3&tanks[]=abc&tanks[]=2&tanks[]=1.5"));

        Assert.Equal([3, 2], page.Tanks);
    }

    [Fact]
    public void Empty_elements_are_left_out()
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=&drivers[]=a&drivers[]=&drivers[]=b"));

        Assert.Equal(["a", "b"], page.Drivers);
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
    [InlineData("?tanks[]=abc&tanks[]=x")]
    [InlineData("?tanks[]=&tanks[]=")]
    [InlineData("?tanks[]")]
    [InlineData("?tanks=")]
    public void A_key_with_no_readable_element_keeps_the_initializer(string query)
    {
        var page = new FleetPage();

        PageBinder.Bind(page, NoSegments, QueryString.Parse(query));

        Assert.Equal([7], page.Tanks);
    }

    [Fact]
    public void Going_back_to_an_address_without_the_key_restores_the_initializer()
    {
        var page = new FleetPage();
        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=ada&drivers[]=theo"));

        var changed = PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.True(changed);
        Assert.Empty(page.Drivers);
    }

    [Fact]
    public void The_same_elements_bound_again_are_no_change_and_keep_the_list()
    {
        var page = new FleetPage();
        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=ada&drivers[]=theo"));
        var first = page.Drivers;

        var changed = PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[0]=ada&drivers[1]=theo"));

        Assert.False(changed);
        Assert.Same(first, page.Drivers);
    }

    [Fact]
    public void Other_elements_bound_again_are_a_change()
    {
        var page = new FleetPage();
        PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=ada&drivers[]=theo"));

        var changed = PageBinder.Bind(page, NoSegments, QueryString.Parse("?drivers[]=ada"));

        Assert.True(changed);
        Assert.Equal(["ada"], page.Drivers);
    }

    [Fact]
    public void An_array_and_an_enumerable_bind_as_a_read_only_list_does()
    {
        var page = new ShapesPage();
        var query = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["days[]"] = new(["2026-10-21", "nope", "2026-10-22"]),
            ["names[]"] = new(["a", "b"]),
            ["sizes[0]"] = "1",
            ["sizes[1]"] = "2",
        });

        PageBinder.Bind(page, NoSegments, query);

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
        [QueryParam] public IReadOnlyList<PageBinderTests.CustomerId> Customers { get; set; } = [];
        protected override Component? Render() => Span;
    }
}
