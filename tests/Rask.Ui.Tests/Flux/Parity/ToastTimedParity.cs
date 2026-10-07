using Rask.Core;
using Rask.Core.Messaging;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     Toasts that count down, for <c>scripts/flux/parity-toast.mjs</c> to time against Flux's.
/// </summary>
/// <remarks>
///     <see cref="ToastShownParity" /> holds every toast still so it can be measured. These run: the script
///     gives the page Rask's runtime, which is what counts a toast down (<c>data-rask-dismiss-after</c>), and
///     holds the pointer over the stack and focus inside a toast as it does on Flux's live page — where a
///     hovered group holds every toast in it, each then runs out its own remainder, and focus holds nothing.
///     No other script compares this page: it is not one of Flux's.
/// </remarks>
public sealed partial class ToastTimedParity : FluxParity
{
    // The static page has no Rask runtime to honour data-rask-popover-open, so each example shows its own.
    private const string Show = "<script>document.currentScript.previousElementSibling.showPopover()</script>";

    public override string Page => "toast-timed";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Two durations, as two toasts raised half a second apart have two remainders.
        yield return ("stack", Shown(UiToast.Draw(
            [Say("First", 1, 3000), Say("Second", 2, 4000)],
            static _ => { },
            new UiToastLook(Ui.ToastPosition.BottomEnd, false, null, UiToast.DefaultDuration, new UiToastStack(null, false, null)))));

        yield return ("alone", Shown(UiToast.Draw(
            [Say("Focus", 3, 2000)],
            static _ => { },
            new UiToastLook(Ui.ToastPosition.TopStart, false, null, UiToast.DefaultDuration, null))));
    }

    private static ToastMessage Say(string text, int id, int milliseconds) =>
        new(id, ToastLevel.Info, text) { Duration = TimeSpan.FromMilliseconds(milliseconds) };

    private static Component Shown(Component toast) => Div[toast, Raw.Value(Show)];
}
