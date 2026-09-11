using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The panel as a session inside the app's own runtime — the WASM host's panel — driven with no browser: its frames
///     go to a list instead of the drawer's frame.
/// </summary>
public sealed class DevToolsPanelSessionTests
{
    [Fact]
    public async Task The_first_render_sends_the_whole_panel_document()
    {
        using var panel = new TestPanel(new DevToolsFeed());

        await panel.Session.InitialRenderAsync();

        var frame = Assert.Single(panel.Frames());
        Assert.Contains("No traffic yet.", frame, StringComparison.Ordinal);
        Assert.Contains(DevToolsPanelSession.RootId, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traffic_recorded_after_the_panel_opened_reaches_it_as_another_frame()
    {
        var feed = new DevToolsFeed();
        using var panel = new TestPanel(feed);
        await panel.Session.InitialRenderAsync();

        feed.RecordWire(DevToolsWireDirection.Out, "wire-panel-event", 5, Stopwatch.GetTimestamp());

        Assert.True(
            await panel.WaitForFrameContaining("wire-panel-event", TimeSpan.FromSeconds(5)),
            "the panel never sent a frame with the new traffic");
    }

    [Fact]
    public async Task A_closed_panel_sends_nothing_more()
    {
        var feed = new DevToolsFeed();
        var panel = new TestPanel(feed);
        await panel.Session.InitialRenderAsync();

        panel.Dispose();
        feed.RecordWire(DevToolsWireDirection.Out, "wire-after-close", 5, Stopwatch.GetTimestamp());

        Assert.False(
            await panel.WaitForFrameContaining("wire-after-close", DevToolsRefreshGate.Interval * 3),
            "a disposed panel still rendered");
    }

    /// <summary>A panel session over its own small container, the way the WASM host builds one.</summary>
    private sealed class TestPanel : IDisposable
    {
        private readonly List<string> _frames = [];
        private readonly ServiceProvider _services;

        public TestPanel(DevToolsFeed feed)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new RouteState { Path = DevToolsProbe.PanelPrefix + "/" });
            services.AddSingleton<Navigator>();
            services.AddSingleton<IDevToolsInspection>(new FixedInspection(feed));
            _services = services.BuildServiceProvider();

#pragma warning disable RASK014 // The root has no parent render context to construct it through, as on both hosts.
            var root = new RootErrorBoundary(ActivatorUtilities.CreateInstance<DevToolsShell>(_services));
#pragma warning restore RASK014

            Session = new DevToolsPanelSession(root, _services, frame =>
            {
                lock (_frames)
                {
                    _frames.Add(Encoding.UTF8.GetString(frame.Span));
                }

                return ValueTask.CompletedTask;
            });
        }

        public DevToolsPanelSession Session { get; }

        public string[] Frames()
        {
            lock (_frames)
            {
                return [.. _frames];
            }
        }

        public async Task<bool> WaitForFrameContaining(string text, TimeSpan timeout)
        {
            var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                if (Frames().Any(f => f.Contains(text, StringComparison.Ordinal)))
                {
                    return true;
                }

                await Task.Delay(20);
            }

            return Frames().Any(f => f.Contains(text, StringComparison.Ordinal));
        }

        public void Dispose()
        {
            Session.Dispose();
            _services.Dispose();
        }
    }

    private sealed class FixedInspection(DevToolsFeed feed) : IDevToolsInspection
    {
        public DevToolsFeed? Open(string? sessionId, string? token) => feed;
    }
}
