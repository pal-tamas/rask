using System.Globalization;
using Microsoft.Extensions.Primitives;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class PageBinderTests
{
    private static QueryCollection Query(params (string key, string value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.key, p => new StringValues(p.value), StringComparer.OrdinalIgnoreCase);
        return new QueryCollection(dict);
    }

    private static Dictionary<string, string?> Values(params (string key, string value)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }

    [Fact]
    public void Bind_StringFromRouteValue_AssignsProperty()
    {
        var page = new StringPage();

        PageBinder.Bind(page, Values(("name", "alice")), new QueryCollection());

        Assert.Equal("alice", page.Name);
    }

    [Fact]
    public void Bind_IntFromRouteValue_ConvertsAndAssigns()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(("id", "42")), new QueryCollection());

        Assert.Equal(42, page.Id);
    }

    [Fact]
    public void Bind_NullableInt_FromQuery_AssignsValue()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(), Query(("maybe", "7")));

        Assert.Equal(7, page.Maybe);
    }

    [Fact]
    public void Bind_NullableInt_NotPresent_LeavesNull()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(), new QueryCollection());

        Assert.Null(page.Maybe);
    }

    [Fact]
    public void Bind_GuidAndDateTimeAndBool_Convert()
    {
        var page = new TypedPage();
        var token = Guid.NewGuid();

        PageBinder.Bind(page, Values(
                ("token", token.ToString()),
                ("cutoff", "2026-05-05T10:30:00"),
                ("active", "true")),
            new QueryCollection());

        Assert.Equal(token, page.Token);
        Assert.Equal(new DateTime(2026, 5, 5, 10, 30, 0), page.Cutoff);
        Assert.True(page.Active);
    }

    [Fact]
    public void Bind_CaseInsensitive_MatchesPropertyByDifferentCase()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(("ID", "9")), new QueryCollection());

        Assert.Equal(9, page.Id);
    }

    [Fact]
    public void Bind_RouteWinsOverQuery_WhenBothPresent()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(("id", "1")), Query(("id", "999")));

        Assert.Equal(1, page.Id);
    }

    [Fact]
    public void Bind_ConversionFailure_Throws()
    {
        var page = new IntPage();

        Assert.Throws<RouteBindException>(() =>
            PageBinder.Bind(page, Values(("id", "not-a-number")), new QueryCollection()));
    }

    [Fact]
    public void Bind_CustomIParsable_RoundTrips()
    {
        var page = new CustomerPage();

        PageBinder.Bind(page, Values(("id", "C-42")), new QueryCollection());

        Assert.Equal(new CustomerId(42), page.Id);
    }

    [Fact]
    public void Bind_CustomIParsable_FailureThrows()
    {
        var page = new CustomerPage();

        Assert.Throws<RouteBindException>(() =>
            PageBinder.Bind(page, Values(("id", "nope")), new QueryCollection()));
    }

    [Fact]
    public void Bind_DoubleFromQuery_UsesInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var page = new DoublePage();

            PageBinder.Bind(page, Values(), Query(("ratio", "3.14")));

            Assert.Equal(3.14, page.Ratio);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Bind_PropertyWithoutAttributes_DoesNotBind()
    {
        var page = new UnannotatedPage();

        PageBinder.Bind(page, Values(("name", "alice")), Query(("name", "bob")));

        Assert.Null(page.Name);
    }

    [Fact]
    public void Bind_FirstAssignmentFromUnsetProperty_ReportsChanged()
    {
        var page = new StringPage();

        var changed = PageBinder.Bind(page, Values(("name", "alice")), new QueryCollection());

        Assert.True(changed);
        Assert.Equal("alice", page.Name);
    }

    [Fact]
    public void Bind_SecondAssignmentWithSameValue_ReportsUnchanged()
    {
        var page = new StringPage { Name = "alice" };

        var changed = PageBinder.Bind(page, Values(("name", "alice")), new QueryCollection());

        Assert.False(changed);
    }

    [Fact]
    public void Bind_DifferentValue_ReportsChanged()
    {
        var page = new StringPage { Name = "alice" };

        var changed = PageBinder.Bind(page, Values(("name", "bob")), new QueryCollection());

        Assert.True(changed);
        Assert.Equal("bob", page.Name);
    }

    [Fact]
    public void Bind_NoParamsResolved_ReportsUnchanged()
    {
        var page = new StringPage { Name = "alice" };

        var changed = PageBinder.Bind(page, Values(), new QueryCollection());

        Assert.False(changed);
        Assert.Equal("alice", page.Name);
    }

    public readonly record struct CustomerId(int Value) : IParsable<CustomerId>
    {
        public static CustomerId Parse(string s, IFormatProvider? provider)
        {
            if (TryParse(s, provider, out var result))
            {
                return result;
            }

            throw new FormatException($"Invalid CustomerId '{s}'.");
        }

        public static bool TryParse(string? s, IFormatProvider? provider, out CustomerId result)
        {
            if (s is not null && s.StartsWith("C-", StringComparison.Ordinal)
                              && int.TryParse(s.AsSpan(2), NumberStyles.Integer, provider, out var v))
            {
                result = new CustomerId(v);
                return true;
            }

            result = default;
            return false;
        }
    }

    [SkipFactory]
    private sealed class CustomerPage : Component
    {
        [RouteParam] public CustomerId Id { get; set; }
        protected override Component Render() => new Span(null);
    }

    [SkipFactory]
    private sealed class DoublePage : Component
    {
        [QueryParam] public double? Ratio { get; set; }
        protected override Component Render() => new Span(null);
    }

    [SkipFactory]
    private sealed class StringPage : Component
    {
        [RouteParam] public string? Name { get; set; }
        protected override Component Render() => new Span(null);
    }

    [SkipFactory]
    private sealed class IntPage : Component
    {
        [RouteParam] public int Id { get; set; }
        [QueryParam] public int? Maybe { get; set; }
        protected override Component Render() => new Span(null);
    }

    [SkipFactory]
    private sealed class TypedPage : Component
    {
        [RouteParam] public Guid Token { get; set; }
        [RouteParam] public DateTime Cutoff { get; set; }
        [RouteParam] public bool Active { get; set; }
        protected override Component Render() => new Span(null);
    }

    [SkipFactory]
    private sealed class UnannotatedPage : Component
    {
        public string? Name { get; set; }
        protected override Component Render() => new Span(null);
    }
}
