using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     The forms guide's "asking before an action" demo: each row's buttons carry the question the browser asks, and
///     the press it guards deletes the row once it arrives.
/// </summary>
/// <remarks>The dialogs themselves are <c>ConfirmWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.</remarks>
public sealed partial class FormConfirmDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Each_row_asks_before_it_is_deleted_and_the_prompt_waits_for_its_name()
    {
        var page = Page.Render(() => FormConfirmDemo, TestServices.Default());

        var delete = page.Find("#fc-delete-Acme");
        var wipe = page.Find("#fc-wipe-Acme");

        Assert.Equal("Delete Acme?", delete.Attribute("data-rask-confirm"));
        Assert.Null(delete.Attribute("data-rask-confirm-expect"));
        Assert.Equal("Type Acme to delete it", wipe.Attribute("data-rask-confirm"));
        Assert.Equal("Acme", wipe.Attribute("data-rask-confirm-expect"));
    }

    [Fact]
    public async Task A_press_that_arrives_deletes_its_row()
    {
        var page = Page.Render(() => FormConfirmDemo, TestServices.Default());

        await page.On("#fc-delete-Globex").Click();

        Assert.Contains("2 partners", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("fc-delete-Globex", page.Html, StringComparison.Ordinal);
    }
}
