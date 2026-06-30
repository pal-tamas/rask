using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("context")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ContextPage : Component
{
    protected override RenderResult Head => Title()["Context — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Context",
            "Supply a value to a whole subtree and read it deep down — no prop drilling. Rask's analogue of React Context / Blazor CascadingValue, with the provider and readers on one Context type."),
        H2(Class: "h4 mt-4 mb-3")["Provide once, consume anywhere below"],
        CodeSample(
            ["ContextThemeDemo.cs", "ThemeCard.cs", "ThemeBadge.cs"],
            Notes:
            "The badge sits inside ThemeCard, which receives no theme parameter and is render-cached after first paint. Reading the value with Context.Required opts the badge out of the render cache, so each toggle re-renders only the badge — straight through the cached intermediate, with nothing threaded between them.",
            Result: ContextThemeDemo()),
        H2(Class: "h4 mt-5 mb-3")["How it works"],
        Ul(Class: "text-secondary")[
            Li()[
                "Context.Provide<T>(value) is a transparent node — it renders its children with no wrapper and pushes the value onto an ambient stack for the duration of that subtree."],
            Li()[
                "Nested providers of the same type resolve nearest-first; an optional Name lets two providers of one type coexist."],
            Li()[
                "Reading is explicit (like React's useContext) rather than an attribute, so it composes anywhere a component renders — even outside a form or route."],
            Li()[
                "Change detection rides the normal render walk: when the value changes the providing component re-renders, and consumers (which bypass the cache) re-read on the way down."]
        ],
        SeeAlso.Guides(("composition", "Composition"))
    ];
}
