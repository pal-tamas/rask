using System.Globalization;
using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

public partial class PageBinderTests : global::Rask.Core.RaskMarkup
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
    public void A_string_route_value_is_assigned_to_its_property()
    {
        var page = new StringPage();

        PageBinder.Bind(page, Values(("name", "alice")), new QueryCollection());

        Assert.Equal("alice", page.Name);
    }

    [Fact]
    public void An_int_route_value_is_converted_and_assigned()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(("id", "42")), new QueryCollection());

        Assert.Equal(42, page.Id);
    }

    [Fact]
    public void A_nullable_int_is_assigned_from_the_query()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(), Query(("maybe", "7")));

        Assert.Equal(7, page.Maybe);
    }

    [Fact]
    public void An_absent_nullable_int_is_left_null()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(), new QueryCollection());

        Assert.Null(page.Maybe);
    }

    [Fact]
    public void Guid_DateTime_and_bool_values_are_converted()
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
    public void A_value_matches_its_property_in_a_different_case()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(("ID", "9")), new QueryCollection());

        Assert.Equal(9, page.Id);
    }

    [Fact]
    public void The_route_value_wins_over_the_query_when_both_are_present()
    {
        var page = new IntPage();

        PageBinder.Bind(page, Values(("id", "1")), Query(("id", "999")));

        Assert.Equal(1, page.Id);
    }

    [Fact]
    public void A_conversion_failure_throws()
    {
        var page = new IntPage();

        Assert.Throws<RouteBindException>(() =>
            PageBinder.Bind(page, Values(("id", "not-a-number")), new QueryCollection()));
    }

    [Fact]
    public void A_custom_IParsable_value_round_trips()
    {
        var page = new CustomerPage();

        PageBinder.Bind(page, Values(("id", "C-42")), new QueryCollection());

        Assert.Equal(new CustomerId(42), page.Id);
    }

    [Fact]
    public void A_custom_IParsable_value_that_fails_to_parse_throws()
    {
        var page = new CustomerPage();

        Assert.Throws<RouteBindException>(() =>
            PageBinder.Bind(page, Values(("id", "nope")), new QueryCollection()));
    }

    [Fact]
    public void A_double_from_the_query_is_parsed_with_the_invariant_culture()
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
    public void A_property_without_attributes_does_not_bind()
    {
        var page = new UnannotatedPage();

        PageBinder.Bind(page, Values(("name", "alice")), Query(("name", "bob")));

        Assert.Null(page.Name);
    }

    [Fact]
    public void The_first_assignment_to_an_unset_property_reports_a_change()
    {
        var page = new StringPage();

        var changed = PageBinder.Bind(page, Values(("name", "alice")), new QueryCollection());

        Assert.True(changed);
        Assert.Equal("alice", page.Name);
    }

    [Fact]
    public void A_second_assignment_of_the_same_value_reports_no_change()
    {
        var page = new StringPage { Name = "alice" };

        var changed = PageBinder.Bind(page, Values(("name", "alice")), new QueryCollection());

        Assert.False(changed);
    }

    [Fact]
    public void A_different_value_reports_a_change()
    {
        var page = new StringPage { Name = "alice" };

        var changed = PageBinder.Bind(page, Values(("name", "bob")), new QueryCollection());

        Assert.True(changed);
        Assert.Equal("bob", page.Name);
    }

    [Fact]
    public void With_no_parameters_resolved_a_bound_property_resets_to_default()
    {
        // Multi-route pages reuse the same Component instance across templates that
        // bind different parameter sets — `/todos/{id}/edit` sets Id, then navigating
        // to `/todos` (with no `id` segment) MUST clear Id so EditingItem stops
        // resolving and the dialog closes. Sticky binding is the bug; reset-to-default
        // is the contract.
        var page = new StringPage { Name = "alice" };

        var changed = PageBinder.Bind(page, Values(), new QueryCollection());

        Assert.True(changed);
        Assert.Null(page.Name);
    }

    [Fact]
    public void With_no_parameters_resolved_a_property_already_at_default_reports_no_change()
    {
        // The reset still goes through, but Equals(null, null) means no change is
        // reported — keeps render-cache invalidation tied to a real diff.
        var page = new StringPage();

        var changed = PageBinder.Bind(page, Values(), new QueryCollection());

        Assert.False(changed);
        Assert.Null(page.Name);
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
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class DoublePage : Component
    {
        [QueryParam] public double? Ratio { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class StringPage : Component
    {
        [RouteParam] public string? Name { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class IntPage : Component
    {
        [RouteParam] public int Id { get; set; }
        [QueryParam] public int? Maybe { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class TypedPage : Component
    {
        [RouteParam] public Guid Token { get; set; }
        [RouteParam] public DateTime Cutoff { get; set; }
        [RouteParam] public bool Active { get; set; }
        protected override Component? Render() => Span;
    }

    [SkipFactory]
    private sealed class UnannotatedPage : Component
    {
        public string? Name { get; set; }
        protected override Component? Render() => Span;
    }
}
