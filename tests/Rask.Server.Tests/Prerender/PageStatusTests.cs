using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// The status a rendered page answers with, pinned as a truth table: a fault outranks the page, and the
// page outranks the router.
public class PageStatusTests
{
    [Theory]
    [InlineData(false, null, false, false, 200)]
    [InlineData(false, null, true, false, 404)] // the not-found page, actually mounted
    [InlineData(false, 200, true, false, 200)] // a deliberate soft 404
    [InlineData(false, 410, false, false, 410)] // a page's own status
    [InlineData(true, 200, true, false, 500)] // a page that threw does not get to claim it succeeded
    [InlineData(false, null, false, true, 403)] // the forbidden sentence, drawn in the app's layout
    [InlineData(false, 200, false, true, 403)] // a layout's own status does not hide that the page was refused
    [InlineData(true, null, false, true, 500)]
    public void A_fault_wins_over_the_page_and_the_page_over_the_router(
        bool faulted, int? declaredStatus, bool notFoundMounted, bool forbidden, int expected)
    {
        var status = PageStatus.Of(faulted, declaredStatus, notFoundMounted, forbidden);

        Assert.Equal(expected, status);
    }
}
