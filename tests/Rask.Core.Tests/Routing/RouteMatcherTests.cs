using Microsoft.AspNetCore.Http;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public partial class RouteMatcherTests : global::Rask.Core.RaskMarkup
{
    private static IReadOnlyList<RouteLeaf> Flat(params Route[] routes) => RouteFlattener.Flatten(routes);

    [Fact]
    public void The_literal_root_matches()
    {
        var leaves = Flat(new Route(typeof(HomePage), "/"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/"), out var chain, out var values));
        Assert.Equal(typeof(HomePage), chain[0]);
        Assert.Empty(values);
    }

    [Fact]
    public void A_single_parameter_binds_its_value()
    {
        var leaves = Flat(new Route(typeof(UserPage), "/users/{id}"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/users/42"), out var chain, out var values));
        Assert.Equal(typeof(UserPage), chain[0]);
        Assert.Equal("42", values["id"]);
    }

    [Fact]
    public void Multiple_parameters_all_bind()
    {
        var leaves = Flat(new Route(typeof(OrgUserPage), "/orgs/{org}/users/{id}"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/orgs/acme/users/42"), out _, out var values));
        Assert.Equal("acme", values["org"]);
        Assert.Equal("42", values["id"]);
    }

    [Fact]
    public void A_path_nothing_matches_does_not_match()
    {
        var leaves = Flat(new Route(typeof(HomePage), "/"));

        Assert.False(RouteMatcher.TryMatch(leaves, new PathString("/missing"), out _, out _));
    }

    [Fact]
    public void A_literal_wins_over_a_parameter()
    {
        var leaves = Flat(
            new Route(typeof(UserPage), "/users/{id}"),
            new Route(typeof(UserNew), "/users/new"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/users/new"), out var chain, out _));
        Assert.Equal(typeof(UserNew), chain[0]);
    }

    [Fact]
    public void A_nested_subroute_matches_the_full_chain_with_merged_values()
    {
        var leaves = Flat(new Route(typeof(DashPage), "/dashboard",
            new[] { new Route(typeof(DashOverview), "overview/{tab}") }));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/dashboard/overview/billing"), out var chain,
            out var values));
        Assert.Equal(new[] { typeof(DashPage), typeof(DashOverview) }, chain);
        Assert.Equal("billing", values["tab"]);
    }

    [Fact]
    public void An_index_subroute_matches_the_bare_parent()
    {
        var leaves = Flat(new Route(typeof(DashPage), "/dashboard",
            new[] { new Route(typeof(DashHome), ""), new Route(typeof(DashOverview), "overview") }));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/dashboard"), out var chain, out _));
        Assert.Equal(new[] { typeof(DashPage), typeof(DashHome) }, chain);
    }

    [Theory]
    [InlineData("int", "42", true)]
    [InlineData("int", "-7", true)]
    [InlineData("int", "007", true)]
    [InlineData("int", "+5", true)]
    [InlineData("int", "2147483647", true)]
    [InlineData("int", "2147483648", false)]
    [InlineData("int", "99999999999999999999", false)]
    [InlineData("int", "abc", false)]
    [InlineData("int", "1.5", false)]
    [InlineData("int", "1,000", false)]
    [InlineData("long", "99999999999", true)]
    [InlineData("long", "-9223372036854775808", true)]
    [InlineData("long", "99999999999999999999", false)]
    [InlineData("long", "12x", false)]
    [InlineData("bool", "true", true)]
    [InlineData("bool", "False", true)]
    [InlineData("bool", "yes", false)]
    [InlineData("bool", "1", false)]
    [InlineData("guid", "0b1f4a52-6f0e-4c53-9a39-3e2f0c1d7a10", true)]
    [InlineData("guid", "0b1f4a526f0e4c539a393e2f0c1d7a10", true)]
    [InlineData("guid", "not-a-guid", false)]
    [InlineData("slug", "anything-at-all", true)]
    public void A_constrained_segment_matches_only_text_that_is_what_the_constraint_says(string constraint, string segment, bool matches)
    {
        var leaves = Flat(new Route(typeof(UserPage), "/items/{id:" + constraint + "}"));

        var matched = RouteMatcher.TryMatch(leaves, new PathString("/items/" + segment), out _, out var values);

        Assert.Equal(matches, matched);
        Assert.Equal(matches ? segment : null, values.GetValueOrDefault("id"));
    }

    [Theory]
    [InlineData("/typed/42/2026-10-12/19.90", true)]
    [InlineData("/typed/99999999999999999999/2026-10-12/19.90", false)]
    [InlineData("/typed/42/2026-13-45/19.90", false)]
    [InlineData("/typed/42/2026-10-12/a-lot", false)]
    public void An_unconstrained_segment_matches_only_text_its_pages_property_can_hold(string path, bool matches)
    {
        var leaves = Flat(new Route(typeof(TypedPage), "/typed/{id}/{day}/{amount}"));

        var matched = RouteMatcher.TryMatch(leaves, new PathString(path), out _, out _);

        Assert.Equal(matches, matched);
    }

    [Fact]
    public void A_segment_its_route_cannot_read_falls_to_the_next_route_that_can()
    {
        var leaves = Flat(
            new Route(typeof(UserPage), "/items/{id:int}"),
            new Route(typeof(OrgUserPage), "/items/{**rest}"));

        var overflowing = RouteMatcher.TryMatch(leaves, new PathString("/items/99999999999999999999"), out var fallen, out _);
        var readable = RouteMatcher.TryMatch(leaves, new PathString("/items/7"), out var chain, out _);

        Assert.True(overflowing);
        Assert.Equal(typeof(OrgUserPage), fallen[0]);
        Assert.True(readable);
        Assert.Equal(typeof(UserPage), chain[0]);
    }

    [Fact]
    public void An_optional_constrained_segment_left_out_still_matches()
    {
        var leaves = Flat(new Route(typeof(UserPage), "/items/{id:int?}"));

        var matched = RouteMatcher.TryMatch(leaves, new PathString("/items"), out _, out var values);

        Assert.True(matched);
        Assert.Null(values["id"]);
    }

    [SkipFactory]
    private sealed class TypedPage : Component
    {
        [RouteParam]
        public int Id { get; set; }

        [RouteParam]
        public DateOnly Day { get; set; }

        [RouteParam]
        public decimal Amount { get; set; }

        protected override Component? Render() => Span;
    }

    private sealed partial class HomePage : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class UserPage : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class UserNew : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class OrgUserPage : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class DashPage : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class DashHome : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class DashOverview : Component
    {
        protected override Component? Render() => Span;
    }
}
