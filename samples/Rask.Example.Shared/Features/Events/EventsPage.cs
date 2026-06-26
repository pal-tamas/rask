using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("events")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class EventsPage : Component
{
    protected override RenderResult Head => Title()["Events — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Events",
            "Event handlers are plain delegates on the factory call site. Every element exposes the full DOM "
            + "GlobalEventHandlers surface — mouse, pointer, touch, wheel, focus, clipboard, keyboard, drag — "
            + "as typed sync OnX + async OnXAsync pairs, each handler triggering a re-render after it runs."),
        H2(Class: "h4 mt-4 mb-3")["The full event surface — one component, zero StateHasChanged"],
        CodeSample(
            ["EventsDemo.cs"],
            Notes:
            "Every handler just mutates a field; the framework re-renders the component that owns the callback "
            + "(the lambda's `this`), so the readouts update on their own. MouseEventArgs carries "
            + "button/coords/modifiers, WheelEventArgs adds deltas, ClipboardEventArgs the pasted text. Wiring "
            + "both OnX and OnXAsync for one event is a compile error (RASK027) — pick one. Audio/Video also "
            + "expose the HTMLMediaElement events (OnPlay/OnTimeUpdate/…) with MediaEventArgs.",
            Result: EventsDemo()),
        H2(Class: "h4 mt-5 mb-3")["Click"],
        CodeSample(
            ["EventsClickDemo.cs"],
            Result: EventsClickDemo()),
        H2(Class: "h4 mt-5 mb-3")["Input — onInput"],
        CodeSample(
            ["EventsInputDemo.cs"],
            Result: EventsInputDemo()),
        H2(Class: "h4 mt-5 mb-3")["Select — onChange"],
        CodeSample(
            ["EventsSelectDemo.cs"],
            Result: EventsSelectDemo()),
        H2(Class: "h4 mt-5 mb-3")["Form — onSubmit"],
        CodeSample(
            ["EventsFormDemo.cs"],
            Notes: "OnSubmit receives a FormData object collected from all named form fields.",
            Result: EventsFormDemo())
    ];
}
