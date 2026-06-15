using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("element-ref")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementRefPage : Component
{
    protected override RenderResult Head => Title()["Element refs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Element refs",
            "Hand a rendered DOM element to JavaScript — for third-party widgets (charts, datepickers, editors) that need the raw node, or for focus and measurement. Rask's analogue of Blazor's ElementReference."),
        H2(Class: "h4 mt-4 mb-3")["Attach a ref, pass it to JS"],
        CodeSample(
            // The real component source and its sibling scoped JS, embedded verbatim — one tab
            // per file shows exactly what produces the live result on the right.
            ["ElementRefDemo.cs", "ElementRefDemo.js"],
            Notes:
            "An ElementRef stamps data-rask-ref on the element. When you pass it to IJSRuntime, the client's JSON reviver swaps it for document.querySelector('[data-rask-ref=…]'), so your JS function receives the live element — no marker-class convention needed. FocusAsync/BlurAsync/ScrollIntoViewAsync are built in.",
            Result: ElementRefDemo()),
        H2(Class: "h4 mt-5 mb-3")["How it works"],
        Ul(Class: "text-secondary")[
            Li()[
                "ElementRef is a reference type minted by ElementRef.New() — allocated once (typically a field), so refs cost nothing on the render hot path; the Ref: parameter is a cheap nullable-reference factory param on every element."],
            Li()[
                "It serializes to {\"__raskRef__\":\"id\"}; both runtimes' revivers resolve that to the DOM element by [data-rask-ref], on Server (over the WS jsInvokes path) and WASM alike."],
            Li()[
                "The built-in __raskEl helpers (focus/blur/scrollIntoView) receive the resolved element, so common operations need no user JS at all."]
        ]
    ];
}
