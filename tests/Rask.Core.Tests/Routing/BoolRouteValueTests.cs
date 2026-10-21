using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;
using PathString = Microsoft.AspNetCore.Http.PathString;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// An old bookmark says `?ibc=1`: a bool reads true/false in any case and 1/0, as Laravel's boolean rule does.
public partial class BoolRouteValueTests : global::Rask.Core.RaskMarkup
{
    private static readonly Dictionary<string, string?> NoSegments = new(StringComparer.Ordinal);

    private static QueryCollection Query(string value) =>
        new(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["flag"] = value });

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void A_bool_query_value_reads_true_false_in_any_case_and_one_or_zero(string text, bool expected)
    {
        var page = new FlagPage();

        PageBinder.Bind(page, NoSegments, Query(text));

        Assert.Equal(expected, page.Flag);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("2")]
    [InlineData("")]
    public void A_bool_query_value_that_is_not_true_false_one_or_zero_is_read_as_absent(string text)
    {
        var page = new FlagPage();

        PageBinder.Bind(page, NoSegments, Query(text));

        Assert.Null(page.Flag);
    }

    [Fact]
    public void A_bool_route_segment_of_one_matches_and_binds_true()
    {
        var leaves = RouteFlattener.Flatten([new Route(typeof(SegmentPage), "/flags/{flag:bool}")]);
        var page = new SegmentPage();

        var matched = RouteMatcher.TryMatch(leaves, new PathString("/flags/1"), out _, out var values);
        PageBinder.Bind(page, values, new QueryCollection());

        Assert.True(matched);
        Assert.True(page.Flag);
    }

    [Fact]
    public void A_bool_read_from_one_is_written_back_into_a_url_as_true()
    {
        var page = new FlagPage();
        PageBinder.Bind(page, NoSegments, Query("1"));

        var written = RouteValueFormatter.Format(page.Flag);

        Assert.Equal("true", written);
    }

    [SkipFactory]
    private sealed class FlagPage : Component
    {
        [QueryParam] public bool? Flag { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class SegmentPage : Component
    {
        [RouteParam] public bool Flag { get; set; }
        protected override Component? Render() => Span;
    }
}
