using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// The Bootstrap Toast example: a reusable Toast component plus the ToastDemo host that shows, stacks and
// dismisses toasts purely through Rask live-diff state — no bootstrap.bundle.js. These drive the live
// click handlers directly (show, dismiss) and assert the rendered markup; auto-hide timing and the full
// browser flow are covered in SharedSmokeTests (Toast branch).
public sealed class ToastDemoTests
{
    [Fact]
    public void Render_Initial_NoToasts_ShowsTriggerAndEmptyState()
    {
        var html = new LiveHost(() => ToastDemo(), TestServices.Default()).RenderAsLiveRoot();

        Assert.DoesNotContain("toast show", html);
        Assert.Contains("Show toast", html);
        Assert.Contains("No toasts", html);
    }

    [Fact]
    public async Task ShowToast_AddsVisibleToast()
    {
        var host = new LiveHost(() => ToastDemo(), TestServices.Default());

        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty()); // "Show toast"

        var html = host.RenderAsLiveRoot();
        Assert.Contains("toast show", html);
        Assert.Contains("Hello, world! This is a toast message.", html);
        Assert.DoesNotContain("No toasts", html);
    }

    [Fact]
    public async Task ShowToast_Twice_StacksTwoToasts()
    {
        var host = new LiveHost(() => ToastDemo(), TestServices.Default());

        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty());
        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty());

        Assert.Equal(2, CountOccurrences(host.RenderAsLiveRoot(), "toast show"));
    }

    [Fact]
    public async Task Dismiss_RemovesToast()
    {
        var host = new LiveHost(() => ToastDemo(), TestServices.Default());
        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty()); // show

        // The toast's × is the last click handler, appended after the trigger/option controls.
        var ids = ClickIds(host.RenderAsLiveRoot());
        await host.TryInvokeHandlerAsync(ids[^1], Empty()); // close

        Assert.DoesNotContain("toast show", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task SuccessButton_RendersColouredToast_WithWhiteClose()
    {
        var host = new LiveHost(() => ToastDemo(), TestServices.Default());

        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[1], Empty()); // "Success"

        var html = host.RenderAsLiveRoot();
        Assert.Contains("text-bg-success", html);
        Assert.Contains("btn-close-white", html);
        Assert.Contains("Your changes were saved.", html);
    }

    [Fact]
    public void Toast_Render_EmitsBootstrapMarkup()
    {
        var html = new LiveHost(
            () => BsToast(Id: 1, Title: "Heads up", Message: "A message", Timestamp: "just now", Icon: BsIconName.Bell),
            TestServices.Default()).RenderAsLiveRoot();

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
        var html = new LiveHost(
            () => BsToast(Id: 1, Title: "Saved", Message: "Done", Color: BsColor.Success),
            TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("toast show align-items-center text-bg-success border-0", html);
        Assert.Contains("<div class=\"d-flex\">", html);
        Assert.Contains("btn-close btn-close-white me-2 m-auto", html);
        Assert.DoesNotContain("toast-header", html); // colour scheme is body + close only
    }

    [Fact]
    public void Toast_Default_NoVariant_NoWhiteClose()
    {
        var html = new LiveHost(
            () => BsToast(Id: 1, Title: "T", Message: "M"),
            TestServices.Default()).RenderAsLiveRoot();

        Assert.DoesNotContain("text-bg", html);
        Assert.DoesNotContain("btn-close-white", html);
    }

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
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
