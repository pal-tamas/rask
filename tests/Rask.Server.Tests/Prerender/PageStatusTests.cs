using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// The status a rendered page answers with, pinned as a truth table: a fault outranks the page, and the
// page outranks the router.
public class PageStatusTests
{
    [Theory]
    [InlineData(false, null, false, 200)]
    [InlineData(false, null, true, 404)] // the not-found page, actually mounted
    [InlineData(false, 200, true, 200)] // a deliberate soft 404
    [InlineData(false, 410, false, 410)] // a page's own status
    [InlineData(true, 200, true, 500)] // a page that threw does not get to claim it succeeded
    public void OrdersFaultOverThePageOverTheRouter(
        bool faulted, int? declaredStatus, bool notFoundMounted, int expected)
    {
        Assert.Equal(expected, PageStatus.Of(faulted, declaredStatus, notFoundMounted));
    }
}
