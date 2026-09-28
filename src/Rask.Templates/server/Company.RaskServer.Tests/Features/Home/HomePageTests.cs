using Rask.Core;

namespace Company.RaskServer.Tests.Features.Home;

// Deriving from RaskMarkup is what puts the app's pages in reach by name, as they are in markup.
public sealed partial class HomePageTests : RaskMarkup
{
    [Fact]
    public void Home_page_greets_the_visitor()
    {
        var page = Page.Render(() => HomePage);

        Assert.Contains("Hello, Rask!", page.Html);
    }
}
