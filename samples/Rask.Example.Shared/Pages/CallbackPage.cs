using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("callback")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class CallbackPage : Component
{
    protected override RenderResult Head => Title()["Callback — Rask"];

    protected override RenderResult Render() =>
        [
            PageHeader.Render(
                "Callback",
                "A typed parent→child callback that re-renders the component which owns it when invoked, and unifies sync and async handlers behind one InvokeAsync."),
            H2(Class: "h4 mt-4 mb-3")["Child emits, parent re-renders"],
            CodeSample(
                """
                // reusable child — declares an optional Callback<int> prop
                public sealed class RatingStars : Component
                {
                    public int Value { get; set; }
                    public Callback<int> OnRate { get; set; }   // optional; defaults to Empty

                    protected override RenderResult Render() => /* clickable stars */
                        Button(OnClick: async () => await OnRate.InvokeAsync(i))[ "★" ];
                }

                // parent — passes a callback that mutates its own state
                RatingStars(Value: _rating,
                            OnRate: Callback.Create<int>(n => _rating = n))
                P()[ $"You rated: {_rating}/5" ]
                """,
                Notes:
                "The star button's handler belongs to RatingStars, so the dispatch only dirties the child. The Callback captures the parent as its receiver, so InvokeAsync re-renders the parent too — the rating line updates without RatingStars knowing the parent exists or the parent wiring StateHasChanged by hand.",
                Result: CallbackRatingDemo()),
            H2(Class: "h4 mt-5 mb-3")["How it works"],
            Ul(Class: "text-secondary")[
                Li()["Callback<T> is a non-nullable struct; its default is the empty no-op callback, so invoke sites never need a null check and the generator makes the prop an optional factory parameter."],
                Li()["Invoking captures the delegate's component (its Target) as the receiver and calls StateHasChanged on it after running — sync or async, awaited uniformly."],
                Li()["When a child simply forwards a parent's delegate straight onto a DOM element, Rask's handler-owner resolution already re-renders the parent; Callback covers the cases where the child wraps, transforms, or invokes it off the DOM path."],
                Li()["Prefer the plain lambda at call sites? Declare an Action<T>?/Func<T,Task>? prop and invoke it with the static helper: await Callback.InvokeAsync(OnRate, i)."]
            ]
        ];
}
