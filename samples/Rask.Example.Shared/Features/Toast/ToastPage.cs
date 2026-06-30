using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("toast")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ToastPage : Component
{
    protected override RenderResult Head => Title()["Toasts — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Toast",
            "Bootstrap toasts via Rask.Bootstrap's BsToast — show, stack, dismiss, place and auto-hide — "
            + "driven entirely by Rask state. No bootstrap.bundle.js, no data-bs-dismiss, no setTimeout."),
        H2(Class: "h4 mt-4 mb-3")["Show, stack, dismiss & auto-hide"],
        CodeSample(
            ["ToastDemo.cs"],
            Notes:
            "BsToast (from Rask.Bootstrap) renders `class=\"toast show\"`, so a toast exists in the tree only "
            + "while visible; the × fires OnClose(Id) — an Action<int> the host binds as a method group "
            + "(OnClose: RemoveToast), so the framework wraps it to re-render the host, which drops it from "
            + "the list. Auto-hide is a one-shot Timer in OnMount, disposed in OnUnmount. Each toast carries a "
            + "Key so the keyed diff tracks identity.",
            Result: ToastDemo()),
        H2(Class: "h4 mt-5 mb-3")["How it works"],
        Ul(Class: "text-secondary")[
            Li()[
                "Show — there is no hidden-then-revealed element. A toast is added to the host's list on click "
                + "and rendered as ", Code()["class=\"toast show\""], "; removing it from the list unmounts it."],
            Li()[
                "Dismiss — the close button is a normal ", Code()["Button(OnClick: …)"],
                ", not ", Code()["data-bs-dismiss=\"toast\""],
                ". It fires ", Code()["OnClose(Id)"], " — an ", Code()["Action<int>"],
                " the host binds as a method group (", Code()["OnClose: RemoveToast"],
                "), so its target is the host and the framework wraps it to re-render the host. A per-toast "
                + "lambda would capture the loop variable instead of the component, and no re-render would fire."],
            Li()[
                "Auto-hide — a one-shot ", Code()["System.Threading.Timer"],
                " started in OnMount fires OnClose after the delay; OnUnmount disposes it, so a hand-dismissed "
                + "toast cancels its own pending timer. No setTimeout, no client JS."],
            Li()[
                "Stack & placement — toasts live in a ", Code()["toast-container"],
                "; each carries a Key for stable identity. The picker swaps Bootstrap position utilities "
                + "(real apps use position-fixed over the viewport; this demo uses position-absolute in a stage)."]
        ]
    ];
}
