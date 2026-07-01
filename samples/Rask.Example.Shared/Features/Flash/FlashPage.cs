using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("flash")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class FlashPage : Component
{
    protected override RenderResult Head => Title()["Flash messages — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Flash messages",
            "Rails-style transient messages via the injectable IFlash service. Queue a message from any "
            + "component or handler; a single FlashOutlet drains it and shows it once. Scoped per session, so "
            + "a message set just before a client-side navigation survives it and appears on the destination."),
        H2(Class: "h4 mt-4 mb-3")["Queue once, show once"],
        CodeSample(
            ["FlashDemo.cs"],
            Notes:
            "FlashDemo injects IFlash through its constructor and calls flash.Success(...) / .Error(...) on "
            + "click. The headless FlashOutlet — subscribed to IFlash.Changed — drains the queue (consumed-once) "
            + "and hands the messages to a Template, rendered here as a dismissible BsAlert stack; the × calls "
            + "the Template's dismiss(id). No StateHasChanged, no client JS.",
            Result: FlashDemo()),
        H2(Class: "h4 mt-5 mb-3")["How it works"],
        Ul(Class: "text-secondary")[
            Li()[
                "Produce — inject ", Code()["IFlash"], " and call ", Code()["flash.Success(\"Saved\")"],
                " (or ", Code()["Info"], " / ", Code()["Warning"], " / ", Code()["Error"], " / ",
                Code()["Add(level, …)"], "). Thread-safe; adding raises ", Code()["Changed"], "."],
            Li()[
                "Survives navigation — ", Code()["IFlash"], " is registered ", Code()["scoped"],
                " per session (a Server WebSocket session or a WASM app), and a client-side ",
                Code()["NavigateTo"], " does not recreate the session — so a message queued before the "
                + "navigation is still there when the destination mounts."],
            Li()[
                "Show once — ", Code()["FlashOutlet"], " calls ", Code()["Consume()"],
                " (which drains the queue) on mount and on ", Code()["Changed"],
                ", so each message is delivered to exactly one outlet and never reappears on a later render."],
            Li()[
                "Headless — ", Code()["FlashOutlet"], " ships no markup; you own it via ",
                Code()["Template"], ". Rask.Bootstrap's ", Code()["BsFlash"],
                " is a ready-made one (a fixed toast-container of BsToasts) — mount a single ",
                Code()["BsFlash()"], " in your app layout."]
        ]
    ];
}
