using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("callback")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class CallbackPage : Component
{
    protected override RenderResult Head => Title()["Callbacks — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Callback",
            "Child→parent events are plain delegate props — no special type. Invoking one re-renders the parent that owns it, automatically."),
        H2(Class: "h4 mt-4 mb-3")["Child emits, parent re-renders"],
        CodeSample(
            ["RatingStars.cs", "CallbackRatingDemo.cs"],
            Notes:
            "The star button's handler belongs to RatingStars, so the DOM dispatch only dirties the child — and the child invokes OnRate off that path. The parent still re-renders because the framework wraps the delegate to re-render its owner (the parent, captured from the lambda's `this`). RatingStars never knows the parent exists, and the parent never wires StateHasChanged by hand.",
            Result: CallbackRatingDemo()),
        H2(Class: "h4 mt-5 mb-3")["How it works"],
        Ul(Class: "text-secondary")[
            Li()[
                "An event-callback prop is any Action / Action<T> / Func<Task> / Func<T,Task> on a component. The generated factory wraps it so invoking it runs your delegate and then re-renders the component that owns it."],
            Li()[
                "The owner is the delegate's Target — the component you wrote the lambda inside (it captures `this`). A static method, or a lambda closing over a local instead of `this`, has no component target, so no auto re-render fires."],
            Li()[
                "When a child forwards a parent's delegate straight onto a DOM element, handler-owner resolution already re-renders the parent; the wrapper covers the cases the child wraps, transforms, or fires off the DOM path."],
            Li()[
                "HTML element handlers (Button.OnClick, …) are not wrapped — they reach the DOM directly, where re-render is already free. Wrapping is confined to your own components, keeping the render hot path allocation-free."]
        ]
    ];
}
