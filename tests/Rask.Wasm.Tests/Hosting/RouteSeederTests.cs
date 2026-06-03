using Rask.Core.Routing;

namespace Rask.Wasm.Tests.Hosting;

public class RouteSeederTests
{
    [Fact]
    public void Seed_RootSlash_StaysSlash()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/", state);

        Assert.Equal("/", state.Path);
        Assert.Same(QueryCollection.Empty, state.Query);
    }

    [Fact]
    public void Seed_PathOnly_NoQuery_PreservesPath()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/widgets/42", state);

        Assert.Equal("/widgets/42", state.Path);
        Assert.Same(QueryCollection.Empty, state.Query);
    }

    [Fact]
    public void Seed_PathWithIndexHtml_StripsSuffix()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/index.html", state);

        Assert.Equal("/", state.Path);
    }

    [Fact]
    public void Seed_NestedPathWithIndexHtml_StripsSuffix()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/foo/bar/index.html", state);

        Assert.Equal("/foo/bar", state.Path);
    }

    [Fact]
    public void Seed_QueryWithLeadingQuestion_ParsedIntoQueryCollection()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/x?a=1&b=two", state);

        Assert.Equal("/x", state.Path);
        Assert.Equal("1", state.Query["a"].ToString());
        Assert.Equal("two", state.Query["b"].ToString());
    }

    [Fact]
    public void Seed_OnlyQuestionMark_QueryRemainsEmpty()
    {
        var state = new RouteState();

        RouteSeeder.Seed("/x?", state);

        Assert.Equal("/x", state.Path);
        Assert.Equal(0, state.Query.Count);
    }

    [Fact]
    public void Seed_EmptyLocation_FallsBackToSlash()
    {
        var state = new RouteState();

        RouteSeeder.Seed(string.Empty, state);

        Assert.Equal("/", state.Path);
    }

    [Fact]
    public void Seed_NullLocation_FallsBackToSlash()
    {
        var state = new RouteState();

        RouteSeeder.Seed(null!, state);

        Assert.Equal("/", state.Path);
    }
}
