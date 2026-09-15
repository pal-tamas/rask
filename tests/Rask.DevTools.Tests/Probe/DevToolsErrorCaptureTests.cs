using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     What a real page's faults put into its feed, through the probe the host installs: a handler that throws, and a render
///     that throws.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsErrorCaptureTests
{
    [Fact]
    public async Task A_handler_that_throws_is_listed_for_its_page_with_the_component_it_belongs_to()
    {
        using var host = DevToolsLivePage.Host<DevToolsErrorsTestApp>();
        var (_, feed, socket, handlers) = await DevToolsLivePage.OpenWithHandlersAsync(host);

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlers[0], type = "click" });

        Assert.True(await DevToolsLivePage.WaitFor(
            () => feed.Errors.Snapshot().Any(e => e.Kind == DevToolsErrorKind.Handler), TimeSpan.FromSeconds(5)),
            "the handler's fault was never listed for its page");
        var error = feed.Errors.Snapshot().Single(e => e.Kind == DevToolsErrorKind.Handler);
        Assert.Equal("InvalidOperationException", error.Title);
        Assert.Equal("the handler failed on purpose", error.Message);
        // Innermost last, under the boundaries every app is wrapped in, as the Tree tab shows them.
        Assert.Equal(nameof(DevToolsErrorsTestApp), error.Path[^1]);
        Assert.False(error.AppWide);
        // Every app is wrapped in the root error boundary, which takes a handler's fault.
        Assert.True(error.Caught);
        socket.Dispose();
    }

    [Fact]
    public async Task A_render_that_throws_is_listed_with_the_components_it_happened_inside()
    {
        using var host = DevToolsLivePage.Host<DevToolsErrorsTestApp>();
        var (_, feed, socket, handlers) = await DevToolsLivePage.OpenWithHandlersAsync(host);

        await socket.SendJsonAsync(new { id = handlers[1], type = "click" });

        Assert.True(await DevToolsLivePage.WaitFor(
            () => feed.Errors.Snapshot().Any(e => e.Kind == DevToolsErrorKind.Render), TimeSpan.FromSeconds(5)),
            "the render fault was never listed for its page");
        var error = feed.Errors.Snapshot().Single(e => e.Kind == DevToolsErrorKind.Render);
        Assert.Equal("the render failed on purpose", error.Message);
        Assert.Equal(nameof(DevToolsThrowingChild), error.Path[^1]);
        Assert.Contains(nameof(DevToolsTestFrame), error.Path);
        Assert.Contains(nameof(DevToolsErrorsTestApp), error.Path);
        Assert.False(error.AppWide);
        Assert.NotNull(error.ComponentId);
        socket.Dispose();
    }
}
