using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="ClipboardDemo" /> (<c>IClipboard</c>).</summary>
[Route("browser/clipboard")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ClipboardPage : Component
{
    protected override RenderResult Head => Title()["Clipboard — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Clipboard",
            "Copy to and read from the system clipboard via IClipboard (navigator.clipboard)."),
        CodeSample(
            ["ClipboardDemo.cs"],
            Notes: "Browser-gated: needs a secure context and (for reads) a user gesture / permission. Wrap in try/catch.",
            Result: ClipboardDemo())
    ];
}
