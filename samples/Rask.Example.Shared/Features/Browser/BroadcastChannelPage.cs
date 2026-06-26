using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="BroadcastChannelDemo" /> (<c>IBroadcastChannel</c>).</summary>
[Route("browser/broadcast")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BroadcastChannelPage : Component
{
    protected override RenderResult Head => Title()["Broadcast channel — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Broadcast channel",
            "Send messages between same-origin tabs/windows via IBroadcastChannel — for cross-tab sync "
            + "(sign-out, theme, 'data updated'). The pushed message re-renders the component through the "
            + "framework, no manual plumbing. Works on both transports; open this page in a second tab to try it."),
        CodeSample(
            ["BroadcastChannelDemo.cs"],
            Notes: "OpenAsync(name, handler) returns a connection (IAsyncDisposable); PostAsync sends to other "
                + "connections of the same name. The handler is pushed from JS via a static [JSInvokable], so "
                + "one wiring serves both Server and WASM.",
            Result: BroadcastChannelDemo())
    ];
}
