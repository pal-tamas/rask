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
            "Event handlers are plain delegates on the factory call site — OnClick, OnInput, OnChange, OnSubmit. Each handler triggers a re-render after it runs."),
        H2(Class: "h4 mt-4 mb-3")["Click"],
        CodeSample(
            EmbeddedSource.Read("EventsClickDemo.cs"),
            Result: EventsClickDemo()),
        H2(Class: "h4 mt-5 mb-3")["Input — onInput"],
        CodeSample(
            EmbeddedSource.Read("EventsInputDemo.cs"),
            Result: EventsInputDemo()),
        H2(Class: "h4 mt-5 mb-3")["Select — onChange"],
        CodeSample(
            EmbeddedSource.Read("EventsSelectDemo.cs"),
            Result: EventsSelectDemo()),
        H2(Class: "h4 mt-5 mb-3")["Form — onSubmit"],
        CodeSample(
            EmbeddedSource.Read("EventsFormDemo.cs"),
            Notes: "OnSubmit receives a FormData object collected from all named form fields.",
            Result: EventsFormDemo())
    ];
}
