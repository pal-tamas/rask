using Rask.Core.Routing;

namespace Rask.Testing.Tests;

[Route("/visit-tests/new")]
public sealed partial class VisitNewProduct : Component
{
    protected override Component? Render() =>
    [
        H1["New product"],
        Button.OnClick(() => VisitProductList.Go())["Save"],
        A.Href(VisitProductList.Url())["Back to the list"]
    ];
}

[Route("/visit-tests/list")]
public sealed partial class VisitProductList : Component
{
    protected override Component? Render() => H1["All products"];
}

// Test.Visit opens the app at a URL through the real router, so a test walks pages the way a person does.
public sealed class VisitTests
{
    [Fact]
    public void Visiting_a_url_renders_the_page_registered_for_it()
    {
        var page = Test.Visit("/visit-tests/new");

        page.Shows("New product");

        page.IsAt("/visit-tests/new");
    }

    [Fact]
    public async Task A_click_that_navigates_moves_to_the_next_page()
    {
        var page = Test.Visit("/visit-tests/new");

        await page.Click("Save");

        page.Shows("All products");
        page.IsAt("/visit-tests/list");
    }

    [Fact]
    public async Task Following_a_link_moves_to_its_page()
    {
        var page = Test.Visit("/visit-tests/new");

        await page.Click("Back to the list");

        page.IsAt("/visit-tests/list");
    }

    [Fact]
    public void IsAt_fails_saying_where_the_page_is()
    {
        var page = Test.Visit("/visit-tests/new");

        var failure = Assert.Throws<PageException>(() => page.IsAt("/elsewhere"));

        Assert.Contains("but the page is at \"/visit-tests/new\"", failure.Message);
    }
}
