using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>Counts its own render passes, so a test can assert that a render actually happened.</summary>
internal sealed partial class RenderCountingApp : Component
{
    public int RenderCount { get; private set; }

    protected override Component? HeadAssets => Title["render-count"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        RenderCount++;
        return Div[$"renders={RenderCount}"];
    }
}
