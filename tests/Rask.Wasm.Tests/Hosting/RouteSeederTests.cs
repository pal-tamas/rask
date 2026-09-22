using Rask.Core.Routing;

namespace Rask.Wasm.Tests.Hosting;

public class RouteSeederTests
{
    [Fact]
    public void Seeding_the_root_slash_keeps_the_slash()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/", state);

        Assert.Equal("/", state.Path);
        Assert.Same(QueryCollection.Empty, state.Query);
    }

    [Fact]
    public void Seeding_a_path_with_no_query_preserves_the_path()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/widgets/42", state);

        Assert.Equal("/widgets/42", state.Path);
        Assert.Same(QueryCollection.Empty, state.Query);
    }

    [Fact]
    public void Seeding_a_path_ending_in_index_html_strips_the_suffix()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/index.html", state);

        Assert.Equal("/", state.Path);
    }

    [Fact]
    public void Seeding_a_nested_path_ending_in_index_html_strips_the_suffix()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/foo/bar/index.html", state);

        Assert.Equal("/foo/bar", state.Path);
    }

    [Fact]
    public void A_query_with_a_leading_question_mark_is_parsed_into_the_query_collection()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/x?a=1&b=two", state);

        Assert.Equal("/x", state.Path);
        Assert.Equal("1", state.Query["a"].ToString());
        Assert.Equal("two", state.Query["b"].ToString());
    }

    [Fact]
    public void A_lone_question_mark_leaves_the_query_empty()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/x?", state);

        Assert.Equal("/x", state.Path);
        Assert.Equal(0, state.Query.Count);
    }

    [Fact]
    public void An_empty_location_falls_back_to_the_slash()
    {
        var state = new RouteState();

        RouteSeeder.Seed(string.Empty, state);

        Assert.Equal("/", state.Path);
    }

    [Fact]
    public void A_null_location_falls_back_to_the_slash()
    {
        var state = new RouteState();

        RouteSeeder.Seed(null!, state);

        Assert.Equal("/", state.Path);
    }
}
