using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="MediaQueryDemo" /> (<c>IMediaQuery</c>).</summary>
[Route("browser/media-query")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class MediaQueryPage : Component
{
    protected override RenderResult Head => Title()["Media queries — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Media queries",
            "Evaluate CSS media queries and user preferences from C# via IMediaQuery (matchMedia) — viewport "
            + "size, dark mode, reduced motion — to branch component logic the way CSS branches styles."),
        CodeSample(
            ["MediaQueryDemo.cs"],
            Notes: "A one-shot evaluation via the framework's __raskApi.matchMedia helper (returns the "
                + ".matches boolean). Re-read when you need a fresh answer; works on both transports.",
            Result: MediaQueryDemo())
    ];
}
