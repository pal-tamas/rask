using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// `[Route("/items/{count?}")]` with `[RouteParam] public int Count { get; set; } = 5;` — one rule with the
// query parameter: what the address does not carry leaves the property at what the page was built with.
public partial class RouteParamInitializerTests : global::Rask.Core.RaskMarkup
{
    private static readonly Guid Todo = Guid.Parse("3e18ad13-d95e-4808-97bd-918b457c006b");

    // The two ways a segment is not there: an optional one left out is under its name with no value, and a
    // route of a multi-route page that has no such segment does not name it at all.
    private static Dictionary<string, string?> Without(string name, bool optionalLeftOut)
    {
        var segments = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (optionalLeftOut)
        {
            segments[name] = null;
        }

        return segments;
    }

    private static Dictionary<string, string?> With(string name, string value) =>
        new(StringComparer.OrdinalIgnoreCase) { [name] = value };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_segment_left_out_keeps_its_initializer(bool optionalLeftOut)
    {
        var page = new ItemsPage();

        var changed = PageBinder.Bind(page, Without("count", optionalLeftOut), new QueryCollection());

        Assert.False(changed);
        Assert.Equal(5, page.Count);
    }

    [Fact]
    public void A_segment_in_the_address_replaces_the_initializer()
    {
        var page = new ItemsPage();

        var changed = PageBinder.Bind(page, With("count", "7"), new QueryCollection());

        Assert.True(changed);
        Assert.Equal(7, page.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_same_page_going_from_a_segment_back_to_none_restores_the_initializer(bool optionalLeftOut)
    {
        var page = new ItemsPage();
        PageBinder.Bind(page, With("count", "7"), new QueryCollection());

        var changed = PageBinder.Bind(page, Without("count", optionalLeftOut), new QueryCollection());

        Assert.True(changed);
        Assert.Equal(5, page.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_segment_with_no_initializer_is_its_types_default_when_left_out(bool optionalLeftOut)
    {
        var page = new TodosPage();
        PageBinder.Bind(page, With("id", Todo.ToString()), new QueryCollection());
        var editing = page.Id;

        var changed = PageBinder.Bind(page, Without("id", optionalLeftOut), new QueryCollection());

        Assert.Equal(Todo, editing);
        Assert.True(changed);
        Assert.Null(page.Id);
        Assert.Equal(0, page.Step);
    }

    [Fact]
    public void A_value_the_page_assigned_itself_is_not_the_initializer()
    {
        var page = new ItemsPage();
        PageBinder.Bind(page, Without("count", optionalLeftOut: true), new QueryCollection());
        page.Count = 9;

        var changed = PageBinder.Bind(page, Without("count", optionalLeftOut: true), new QueryCollection());

        Assert.True(changed);
        Assert.Equal(5, page.Count);
    }

    [Fact]
    public void Two_pages_of_one_type_each_keep_what_they_were_built_with()
    {
        var first = new BuiltPage(10);
        var second = new BuiltPage(50);
        PageBinder.Bind(first, With("size", "7"), new QueryCollection());
        PageBinder.Bind(second, With("size", "7"), new QueryCollection());

        PageBinder.Bind(first, Without("size", optionalLeftOut: true), new QueryCollection());
        PageBinder.Bind(second, Without("size", optionalLeftOut: true), new QueryCollection());

        Assert.Equal(10, first.Size);
        Assert.Equal(50, second.Size);
    }

    [Fact]
    public void A_segment_and_a_query_parameter_on_one_page_each_keep_their_own_initializer()
    {
        var page = new ItemsPage();
        var sorted = new QueryCollection(
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["sort"] = "price" });
        PageBinder.Bind(page, With("count", "7"), sorted);

        PageBinder.Bind(page, Without("count", optionalLeftOut: true), new QueryCollection());

        Assert.Equal(5, page.Count);
        Assert.Equal("name", page.Sort);
    }

    [Fact]
    public void A_segment_the_page_cannot_read_is_still_not_a_match()
    {
        var unreadable = With("count", "many");

        var canBind = PageBinder.CanBind([typeof(ItemsPage)], unreadable);

        Assert.False(canBind);
    }

    [Theory]
    [InlineData("PAGE")]
    [InlineData("Page")]
    [InlineData("page")]
    public void A_query_key_is_found_whatever_its_case_in_a_collection_that_compares_exactly(string key)
    {
        var page = new PagedPage();
        var exact = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.Ordinal) { [key] = "3" });

        PageBinder.Bind(page, Without("unused", optionalLeftOut: false), exact);

        Assert.Equal(3, page.Page);
    }

    [Fact]
    public void A_path_that_changes_under_the_same_values_is_a_change_and_the_first_one_is_not()
    {
        var page = new TodosPage();
        var none = Without("id", optionalLeftOut: false);

        var first = PageBinder.Bind(page, none, new QueryCollection(), "/todos");
        var moved = PageBinder.Bind(page, none, new QueryCollection(), "/todos/new");
        var stayed = PageBinder.Bind(page, none, new QueryCollection(), "/todos/new");

        Assert.False(first);
        Assert.True(moved);
        Assert.False(stayed);
    }

    [SkipFactory]
    private sealed class ItemsPage : Component
    {
        [RouteParam] public int Count { get; set; } = 5;
        [QueryParam] public string? Sort { get; set; } = "name";
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class TodosPage : Component
    {
        [RouteParam] public Guid? Id { get; set; }
        [RouteParam] public int Step { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class BuiltPage(int size) : Component
    {
        [RouteParam] public int Size { get; set; } = size;
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class PagedPage : Component
    {
        [QueryParam] public int Page { get; set; } = 1;
        protected override Component? Render() => Span;
    }
}
