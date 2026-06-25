using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="CookiesDemo" /> (<c>ICookies</c>).</summary>
[Route("browser/cookies")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class CookiesPage : Component
{
    protected override RenderResult Head => Title()["Cookies — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Cookies",
            "Read/write non-HttpOnly cookies via ICookies (document.cookie) with typed CookieOptions."),
        CodeSample(
            ["CookiesDemo.cs"],
            Notes: "HttpOnly cookies are invisible to JavaScript by design — set those from the server.",
            Result: CookiesDemo())
    ];
}
