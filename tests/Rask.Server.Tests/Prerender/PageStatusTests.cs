using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// The status a rendered page answers with, pinned as a truth table: a fault outranks the page, and the
// page outranks the router.
public class PageStatusTests
{
    [Theory]
    [InlineData(null, null, false, false, 200)]
    [InlineData(null, null, true, false, 404)] // the not-found page, actually mounted
    [InlineData(null, 200, true, false, 200)] // a deliberate soft 404
    [InlineData(null, 410, false, false, 410)] // a page's own status
    [InlineData("fault", 200, true, false, 500)] // a page that threw does not get to claim it succeeded
    [InlineData(null, null, false, true, 403)] // the forbidden sentence, drawn in the app's layout
    [InlineData(null, 200, false, true, 403)] // a layout's own status does not hide that the page was refused
    [InlineData("fault", null, false, true, 500)]
    [InlineData("refusal", 200, false, false, 400)] // the app said no to the visitor: no fault, and no success
    [InlineData("refusal", null, false, true, 400)]
    public void A_fault_wins_over_the_page_and_the_page_over_the_router(
        string? thrown, int? declaredStatus, bool notFoundMounted, bool forbidden, int expected)
    {
        Exception? fault = thrown switch
        {
            "fault" => new InvalidOperationException("boom"),
            "refusal" => new RefusedException("The invoice is already closed."),
            _ => null,
        };

        var status = PageStatus.Of(fault, declaredStatus, notFoundMounted, forbidden);

        Assert.Equal(expected, status);
    }
}
