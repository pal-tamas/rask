using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// A link with filters in it gets mailed around, kept for a year and edited by hand: a value the property
// cannot hold is ordinary input, and is read as if the parameter were not in the URL at all.
[Collection("ConsoleRedirect")]
public partial class UnreadableQueryValueTests : global::Rask.Core.RaskMarkup
{
    private static readonly Dictionary<string, string?> NoSegments = new(StringComparer.Ordinal);

    private static QueryCollection Query(params (string key, string value)[] pairs) =>
        new(pairs.ToDictionary(p => p.key, p => new StringValues(p.value), StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("weightFrom", "abc")]
    [InlineData("weightFrom", "1.5")]
    [InlineData("weightFrom", "99999999999999999999")]
    [InlineData("weightFrom", "")]
    [InlineData("count", "9223372036854775808")]
    [InlineData("count", "ten")]
    [InlineData("price", "1.5.2")]
    [InlineData("price", "cheap")]
    [InlineData("active", "yes")]
    [InlineData("token", "not-a-guid")]
    [InlineData("day", "2026-13-45")]
    [InlineData("since", "yesterday")]
    [InlineData("ratio", "half")]
    [InlineData("customer", "42")]
    [InlineData("page", "two")]
    [InlineData("page", "99999999999999999999")]
    [InlineData("size", "large")]
    [InlineData("size", "")]
    public void A_query_value_its_property_cannot_hold_is_read_as_absent(string name, string unreadable)
    {
        var absent = new FilterPage();
        PageBinder.Bind(absent, NoSegments, new QueryCollection());
        var page = new FilterPage();

        PageBinder.Bind(page, NoSegments, Query((name, unreadable)));

        Assert.Equal(absent.Bound(), page.Bound());
    }

    [Fact]
    public void A_nullable_filter_with_an_unreadable_value_is_null()
    {
        var page = new FilterPage();

        PageBinder.Bind(page, NoSegments, Query(("weightFrom", "abc")));

        Assert.Null(page.WeightFrom);
    }

    [Fact]
    public void A_non_nullable_parameter_with_an_unreadable_value_is_its_types_default()
    {
        var page = new FilterPage();

        PageBinder.Bind(page, NoSegments, Query(("size", "large")));

        Assert.Equal(0, page.Size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("two")]
    public void An_absent_or_unreadable_parameter_keeps_the_propertys_initializer(string? value)
    {
        var page = new FilterPage();

        PageBinder.Bind(page, NoSegments, value is null ? new QueryCollection() : Query(("page", value)));

        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void The_readable_parameters_beside_an_unreadable_one_are_still_bound()
    {
        var page = new FilterPage();

        PageBinder.Bind(page, NoSegments, Query(("weightFrom", "abc"), ("count", "7"), ("size", "25")));

        Assert.Null(page.WeightFrom);
        Assert.Equal(7, page.Count);
        Assert.Equal(25, page.Size);
    }

    [Fact]
    public void A_value_that_turns_unreadable_clears_what_the_last_address_bound()
    {
        var page = new FilterPage();
        PageBinder.Bind(page, NoSegments, Query(("weightFrom", "12")));

        var changed = PageBinder.Bind(page, NoSegments, Query(("weightFrom", "12kg")));

        Assert.True(changed);
        Assert.Null(page.WeightFrom);
    }

    [Fact]
    public void An_unreadable_value_bound_twice_reports_no_change_the_second_time()
    {
        var page = new FilterPage();
        PageBinder.Bind(page, NoSegments, Query(("weightFrom", "abc")));

        var changed = PageBinder.Bind(page, NoSegments, Query(("weightFrom", "abc")));

        Assert.False(changed);
    }

    [Fact]
    public void A_route_segment_handed_straight_to_the_binder_still_throws()
    {
        var page = new SegmentPage();

        var segments = new Dictionary<string, string?>(StringComparer.Ordinal) { ["id"] = "abc" };

        Action bind = () => PageBinder.Bind(page, segments, Query(("id", "5")));

        Assert.Throws<RouteBindException>(bind);
    }

    [Fact]
    public void In_development_the_ignored_parameter_is_said_once_without_its_value()
    {
        RaskDiagnostics.ResetReportOnceForTests();
        var page = new LoggedPage();

        var reported = Reported(true, () =>
        {
            PageBinder.Bind(page, NoSegments, Query(("weightFrom", "s3cret-abc")));
            PageBinder.Bind(page, NoSegments, Query(("weightFrom", "s3cret-abc")));
        });

        var said = Assert.Single(reported, e => string.Equals(e.Category, "Rask.Routing", StringComparison.Ordinal));
        Assert.Equal(RaskLogLevel.Information, said.Level);
        Assert.Contains(nameof(LoggedPage), said.Message, StringComparison.Ordinal);
        Assert.Contains("'weightFrom'", said.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-abc", said.Message, StringComparison.Ordinal);
        Assert.Null(said.Exception);
    }

    [Fact]
    public void In_development_a_list_element_left_out_is_said_once_without_its_value()
    {
        RaskDiagnostics.ResetReportOnceForTests();
        var page = new LoggedListPage();

        var reported = Reported(true, () =>
        {
            PageBinder.Bind(page, NoSegments, Query(("tanks", "3,s3cret-abc")));
            PageBinder.Bind(page, NoSegments, Query(("tanks", "s3cret-abc,2")));
        });

        var said = Assert.Single(reported, e => string.Equals(e.Category, "Rask.Routing", StringComparison.Ordinal));
        Assert.Contains("'Tanks'", said.Message, StringComparison.Ordinal);
        Assert.Contains("Int32", said.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-abc", said.Message, StringComparison.Ordinal);
        Assert.Equal([2], page.Tanks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Outside_development_an_ignored_parameter_says_nothing(bool? development)
    {
        RaskDiagnostics.ResetReportOnceForTests();
        var page = new QuietPage();

        var reported = Reported(development, () => PageBinder.Bind(page, NoSegments, Query(("weightFrom", "abc"))));

        Assert.DoesNotContain(reported, e => string.Equals(e.Category, "Rask.Routing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_host_in_scope_decides_whether_it_is_said_not_the_process_wide_flag(bool hostIsDevelopment)
    {
        RaskDiagnostics.ResetReportOnceForTests();
        var page = new QuietPage();
        var services = new ServiceCollection().AddSingleton(HostMode.For(hostIsDevelopment)).BuildServiceProvider();

        var reported = Reported(!hostIsDevelopment, () =>
        {
            using var host = Ambient.Enter(services);
            PageBinder.Bind(page, NoSegments, Query(("weightFrom", "abc")));
        });

        Assert.Equal(hostIsDevelopment, reported.Exists(static e => string.Equals(e.Category, "Rask.Routing", StringComparison.Ordinal)));
    }

    private static List<RaskDiagnosticEvent> Reported(bool? development, Action act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var sink = RaskDiagnostics.Sink;
        var was = LiveOptions.IsDevelopment;
        RaskDiagnostics.Sink = captured.Add;
        LiveOptions.IsDevelopment = development;
        try
        {
            act();
        }
        finally
        {
            LiveOptions.IsDevelopment = was;
            RaskDiagnostics.Sink = sink;
        }

        return captured;
    }

    [SkipFactory]
    private sealed class FilterPage : Component
    {
        [QueryParam("weightFrom")] public int? WeightFrom { get; set; }
        [QueryParam] public long? Count { get; set; }
        [QueryParam] public decimal? Price { get; set; }
        [QueryParam] public bool? Active { get; set; }
        [QueryParam] public Guid? Token { get; set; }
        [QueryParam] public DateOnly? Day { get; set; }
        [QueryParam] public DateTime? Since { get; set; }
        [QueryParam] public double Ratio { get; set; }
        [QueryParam] public PageBinderTests.CustomerId? Customer { get; set; }
        [QueryParam] public int Page { get; set; } = 1;
        [QueryParam] public int Size { get; set; }

        public object?[] Bound() => [WeightFrom, Count, Price, Active, Token, Day, Since, Ratio, Customer, Page, Size];

        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class SegmentPage : Component
    {
        [RouteParam] public int Id { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class LoggedPage : Component
    {
        [QueryParam("weightFrom")] public int? WeightFrom { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class LoggedListPage : Component
    {
        [QueryParam] public IReadOnlyList<int> Tanks { get; set; } = [];
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class QuietPage : Component
    {
        [QueryParam("weightFrom")] public int? WeightFrom { get; set; }
        protected override Component? Render() => Span;
    }
}
