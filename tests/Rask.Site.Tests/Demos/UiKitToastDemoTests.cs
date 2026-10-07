using System.Text.RegularExpressions;
using Rask.Site.Features.UiKit;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

// The showcase's toast section: every button raises a toast through the Toast facade, and the layout row
// chooses which of Flux's layouts — Ui.Toast, inverted, another corner, a stack — it is shown in.
public sealed partial class UiKitToastDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_raised_toast_shows_in_the_toast_the_demo_places()
    {
        var page = Page.Render(() => UiKitFeedbackDemo, TestServices.Default());

        await page.On("#toast-success").Click();

        Assert.Contains("data-variant=\"success\"", page.Html, StringComparison.Ordinal);
        Assert.Contains(">Post created</div>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_its_own_a_new_toast_takes_the_place_of_the_one_showing()
    {
        var page = Page.Render(() => UiKitFeedbackDemo, TestServices.Default());

        await page.On("#toast-permanent").Click();
        await page.On("#toast-danger").Click();

        Assert.Single(Regex.Matches(page.Html, "data-ui-toast-dialog"));
        Assert.Contains("data-variant=\"danger\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_the_stack_layout_toasts_pile_up_and_survive_the_page_rendering_again()
    {
        var page = Page.Render(() => UiKitFeedbackDemo, TestServices.Default());
        await page.On("#toast-layout-stack").Click();

        await page.On("#toast-permanent").Click();
        await page.On("#toast-permanent").Click();
        await page.Click("+10");

        Assert.Contains("data-ui-toast-group", page.Html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page.Html, "data-ui-toast-dialog").Count);
    }

    [Theory]
    [InlineData("inverted", " data-invert")]
    [InlineData("topend", "data-position=\"top end\"")]
    [InlineData("expanded", " data-expanded")]
    public async Task Each_layout_is_one_of_Fluxs(string layout, string mark)
    {
        var page = Page.Render(() => UiKitFeedbackDemo, TestServices.Default());
        await page.On("#toast-layout-" + layout).Click();

        await page.On("#toast-plain").Click();

        Assert.Contains(mark, page.Html, StringComparison.Ordinal);
    }
}
