using System.Collections.Concurrent;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Core.Rendering;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Configuration;

// Nothing needs this attribute. How far a page climbs is detected from its render, and the detection
// is biased towards keeping a connection — a page wrongly judged interactive behaves exactly as it
// always has, while one wrongly judged static loses its interactivity silently. The attribute is for
// what detection cannot see.
public class RenderModeAttributeTests
{
    [Fact]
    public async Task AComponentDeclaringInteractive_KeepsThePageLive()
    {
        // Renders nothing a walk can observe as needing a connection. Without the declaration this
        // page would be served as a document and its timer would push into nothing.
        using var host = RaskTestHost.Create<DeclaredInteractiveApp>(
            configureServer: o => o.RenderModes.Static = true);

        var body = await host.Http.GetStringAsync("/");

        Assert.Contains("data-rask-root", body);
        Assert.Equal(1, host.Store.Count);
    }

    [Fact]
    public async Task TheDeclarationIsInheritedByEveryPageUsingTheComponent()
    {
        // The point of honouring Interactive from anywhere in the tree: a base component says it once
        // and pages built on it inherit the need without their authors knowing to.
        using var host = RaskTestHost.Create<UsesADeclaredChildApp>(
            configureServer: o => o.RenderModes.Static = true);

        var body = await host.Http.GetStringAsync("/");

        Assert.Contains("data-rask-root", body);
    }

    [Fact]
    public async Task APageDeclaringStatic_IsServedAsADocument()
    {
        // Static is honoured on the routed page even where the app has not turned static pages on at
        // all — that is what "opt down from the ceiling" means.
        using var host = RaskTestHost.Create<StaticDeclaringApp>();

        var body = await host.Http.GetStringAsync("/declared-static");

        Assert.DoesNotContain("data-rask-root", body);
        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task APageDeclaringStaticThatNeedsAConnection_KeepsItAndSaysSo()
    {
        // A request, not a command. Serving this static would leave its button inert, which is the
        // one outcome worth refusing — so the connection wins and the contradiction is reported.
        using var host = RaskTestHost.Create<StaticDeclaringApp>();

        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            var body = await host.Http.GetStringAsync("/declared-static-with-button");

            Assert.Contains("data-rask-root", body);
            Assert.Contains(captured, e =>
                e.Category == "Rask.Ssr" && e.Message.Contains("kept one", StringComparison.Ordinal));
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }
}

public sealed partial class DeclaredInteractiveApp : Component
{
    protected override Component? HeadAssets => Title["declared-interactive"];

    protected override Component? Render() => Div[DeclaredPusher];
}

public sealed partial class UsesADeclaredChildApp : Component
{
    protected override Component? HeadAssets => Title["uses-declared-child"];

    protected override Component? Render() => Div[InheritsTheDeclaration];
}

// The shape detection cannot see: no handler, no form, no ref, no JS call — it pushes on a timer.
[RenderMode(RenderMode.Interactive)]
public partial class DeclaredPusher : Component
{
    protected override Component? Render() => Span["pushes-later"];
}

// Inherited = true on the attribute, so a subclass carries its base's declaration.
public sealed partial class InheritsTheDeclaration : DeclaredPusher;

public sealed partial class StaticDeclaringApp : Component
{
    protected override Component? HeadAssets => Title["static-declaring"];

    protected override Component? Render() => Router;
}

[Route("/declared-static")]
[RenderMode(RenderMode.Static)]
public sealed partial class DeclaredStaticPage : Component
{
    protected override Component? Render() => Div["just-content"];
}

[Route("/declared-static-with-button")]
[RenderMode(RenderMode.Static)]
public sealed partial class DeclaredStaticButContradictoryPage : Component
{
    private int _count;

    protected override Component? Render() => Button.OnClick(() => _count++)[$"count {_count}"];
}
