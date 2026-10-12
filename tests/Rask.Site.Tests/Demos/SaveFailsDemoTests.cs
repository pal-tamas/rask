using Rask.Core.Messaging;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     The "a save that fails" demo, without a browser: the demo handles nothing, and the reader is told all
///     the same.
/// </summary>
public sealed partial class SaveFailsDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_save_that_throws_raises_the_forms_own_toast_and_keeps_what_was_typed()
    {
        var page = Page.Render(() => SaveFailsDemo, TestServices.Default());
        using var toasts = Toast.Fake();
        await page.Type("Wien – Linz").Into("Route");

        await page.Click("Save");

        toasts.Shown("The save failed. Please try again.").Once();
        toasts.Shown("The save failed. Please try again.").As(ToastLevel.Error);
        Assert.Equal("Wien – Linz", page.Find("#v14-name").Attribute("value"));
        Assert.DoesNotContain("30 seconds", page.Html, StringComparison.Ordinal);
    }
}
