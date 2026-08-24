using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

// The Bootstrap Toast example: a reusable Toast component plus the ToastDemo host that shows, stacks and
// dismisses toasts purely through Rask live-diff state — no bootstrap.bundle.js. These drive the live
// click handlers directly (show, dismiss) and assert the rendered markup; auto-hide timing and the full
// browser flow are covered in SharedSmokeTests (Toast branch).
public sealed partial class ToastDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_Initial_NoToasts_ShowsTriggerAndEmptyState()
    {
        var html = RaskTest.Render(() => ToastDemo, TestServices.Default()).Html;

        Assert.DoesNotContain("toast show", html);
        Assert.Contains("Show toast", html);
        Assert.Contains("No toasts", html);
    }

    [Fact]
    public async Task ShowToast_AddsVisibleToast()
    {
        var page = RaskTest.Render(() => ToastDemo, TestServices.Default());

        await page.InvokeAsync(ClickIds(page.Render())[0]); // "Show toast"

        var html = page.Render();
        Assert.Contains("toast show", html);
        Assert.Contains("Hello, world! This is a toast message.", html);
        Assert.DoesNotContain("No toasts", html);
    }

    [Fact]
    public async Task ShowToast_Twice_StacksTwoToasts()
    {
        var page = RaskTest.Render(() => ToastDemo, TestServices.Default());

        await page.InvokeAsync(ClickIds(page.Render())[0]);
        await page.InvokeAsync(ClickIds(page.Render())[0]);

        Assert.Equal(2, CountOccurrences(page.Render(), "toast show"));
    }

    [Fact]
    public async Task Dismiss_RemovesToast()
    {
        var page = RaskTest.Render(() => ToastDemo, TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // show

        // The toast's × is the last click handler, appended after the trigger/option controls.
        var ids = ClickIds(page.Render());
        await page.InvokeAsync(ids[^1]); // close

        Assert.DoesNotContain("toast show", page.Render());
    }

    [Fact]
    public async Task SuccessButton_RendersColouredToast_WithWhiteClose()
    {
        var page = RaskTest.Render(() => ToastDemo, TestServices.Default());

        await page.InvokeAsync(ClickIds(page.Render())[1]); // "Success"

        var html = page.Render();
        Assert.Contains("text-bg-success", html);
        Assert.Contains("btn-close-white", html);
        Assert.Contains("Your changes were saved.", html);
    }

    [Fact]
    public void Toast_Render_EmitsBootstrapMarkup()
    {
        var html = RaskTest.Render(
            () => BsToast.Id(1).Message("A message").Title("Heads up").Timestamp("just now").Icon(BsIconName.Bell),
            TestServices.Default()).Html;

        Assert.Contains("toast show", html);
        Assert.Contains("toast-header", html);
        Assert.Contains("toast-body", html);
        Assert.Contains("btn-close", html);
        Assert.Contains("bi-bell", html);
        Assert.Contains("Heads up", html);
        Assert.Contains("A message", html);
        Assert.Contains("just now", html);
        Assert.Contains("aria-live=\"assertive\"", html);
    }

    [Fact]
    public void Toast_ColouredVariant_UsesHeaderlessFlexLayout()
    {
        var html = RaskTest.Render(
            () => BsToast.Id(1).Message("Done").Title("Saved").Color(BsColor.Success),
            TestServices.Default()).Html;

        Assert.Contains("toast show align-items-center text-bg-success border-0", html);
        Assert.Contains("<div class=\"d-flex\">", html);
        Assert.Contains("btn-close btn-close-white me-2 m-auto", html);
        Assert.DoesNotContain("toast-header", html); // colour scheme is body + close only
    }

    [Fact]
    public void Toast_Default_NoVariant_NoWhiteClose()
    {
        var html = RaskTest.Render(
            () => BsToast.Id(1).Message("M").Title("T"),
            TestServices.Default()).Html;

        Assert.DoesNotContain("text-bg", html);
        Assert.DoesNotContain("btn-close-white", html);
    }

    private static List<string> ClickIds(string html)
    {
        var ids = new List<string>();
        const string marker = "data-rask-on-click=\"";
        var i = 0;
        while ((i = html.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            i += marker.Length;
            var end = html.IndexOf('"', i);
            ids.Add(html[i..end]);
            i = end;
        }

        return ids;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }
}
