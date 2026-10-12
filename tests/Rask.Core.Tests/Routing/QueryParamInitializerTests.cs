using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// `[QueryParam] public int Page { get; set; } = 1;` says what the page shows without `?page=`. A parameter
// the address does not carry — or carries unreadably — leaves the property at what the page was built with.
public partial class QueryParamInitializerTests : global::Rask.Core.RaskMarkup
{
    private static readonly Dictionary<string, string?> NoSegments = new(StringComparer.Ordinal);

    private static QueryCollection Query(params (string key, string value)[] pairs) =>
        new(pairs.ToDictionary(p => p.key, p => new StringValues(p.value), StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void An_absent_parameter_keeps_its_initializer()
    {
        var page = new ListPage();

        var changed = PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.False(changed);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void A_value_in_the_address_replaces_the_initializer()
    {
        var page = new ListPage();

        var changed = PageBinder.Bind(page, NoSegments, Query(("page", "3")));

        Assert.True(changed);
        Assert.Equal(3, page.Page);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public void An_unreadable_value_keeps_the_initializer(string unreadable)
    {
        var page = new ListPage();

        PageBinder.Bind(page, NoSegments, Query(("page", unreadable)));

        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void A_key_with_no_value_at_all_keeps_the_initializer()
    {
        var page = new ListPage();
        var valueless = new QueryCollection(
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["page"] = StringValues.Empty });

        PageBinder.Bind(page, NoSegments, valueless);

        Assert.Equal(1, page.Page);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void The_same_page_going_from_a_value_back_to_none_restores_the_initializer(string? next)
    {
        var page = new ListPage();
        PageBinder.Bind(page, NoSegments, Query(("page", "3")));

        var changed = PageBinder.Bind(page, NoSegments, next is null ? new QueryCollection() : Query(("page", next)));

        Assert.True(changed);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void An_absent_parameter_bound_twice_reports_no_change_the_second_time()
    {
        var page = new ListPage();
        PageBinder.Bind(page, NoSegments, Query(("page", "3")));
        PageBinder.Bind(page, NoSegments, new QueryCollection());

        var changed = PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.False(changed);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void A_parameter_with_no_initializer_is_its_types_default_when_absent()
    {
        var page = new ListPage();
        PageBinder.Bind(page, NoSegments, Query(("offset", "40"), ("limit", "10"), ("search", "tea")));

        PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.Equal(0, page.Offset);
        Assert.Null(page.Limit);
        Assert.Null(page.Search);
    }

    [Fact]
    public void A_nullable_parameter_with_an_initializer_keeps_it_when_absent()
    {
        var page = new ListPage();
        PageBinder.Bind(page, NoSegments, Query(("perPage", "50"), ("sort", "price")));

        PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.Equal(25, page.PerPage);
        Assert.Equal("name", page.Sort);
    }

    [Fact]
    public void A_value_the_page_assigned_itself_is_not_the_initializer()
    {
        var page = new ListPage();
        PageBinder.Bind(page, NoSegments, new QueryCollection());
        page.Page = 9;

        var changed = PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.True(changed);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void Two_pages_of_one_type_each_keep_what_they_were_built_with()
    {
        var first = new BuiltPage(10);
        var second = new BuiltPage(50);
        PageBinder.Bind(first, NoSegments, Query(("size", "7")));
        PageBinder.Bind(second, NoSegments, Query(("size", "7")));

        PageBinder.Bind(first, NoSegments, new QueryCollection());
        PageBinder.Bind(second, NoSegments, new QueryCollection());

        Assert.Equal(10, first.Size);
        Assert.Equal(50, second.Size);
    }

    [Fact]
    public void An_initializer_that_is_an_object_comes_back_as_that_same_object_not_a_copy()
    {
        var page = new TagsPage();
        var built = page.Tags;
        PageBinder.Bind(page, NoSegments, Query(("tags", "a,b")));
        built.Names.Add("added-later");

        PageBinder.Bind(page, NoSegments, new QueryCollection());

        Assert.Same(built, page.Tags);
        Assert.Equal(["all", "added-later"], page.Tags.Names);
    }

    [SkipFactory]
    private sealed class ListPage : Component
    {
        [QueryParam] public int Page { get; set; } = 1;
        [QueryParam("perPage")] public int? PerPage { get; set; } = 25;
        [QueryParam] public string? Sort { get; set; } = "name";
        [QueryParam] public int Offset { get; set; }
        [QueryParam] public int? Limit { get; set; }
        [QueryParam] public string? Search { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class BuiltPage(int size) : Component
    {
        [QueryParam] public int Size { get; set; } = size;
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class TagsPage : Component
    {
        [QueryParam] public TagList Tags { get; set; } = new(["all"]);
        protected override Component? Render() => Span;
    }

    private sealed class TagList(List<string> names) : IParsable<TagList>
    {
        public List<string> Names { get; } = names;

        public static TagList Parse(string s, IFormatProvider? provider) => new([.. s.Split(',')]);

        public static bool TryParse(string? s, IFormatProvider? provider, out TagList result)
        {
            result = new TagList(s is null ? [] : [.. s.Split(',')]);
            return s is not null;
        }
    }
}
