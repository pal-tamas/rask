using Rask.Core;
using Rask.Core.Messaging;
using Rask.Core.Routing;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     The toast itself — which <c>fluxui.dev/components/toast</c> only shows once a button is pressed.
/// </summary>
/// <remarks>
///     <para>
///     <see cref="ToastParity" /> is that page as it LOADS: ten buttons. This is what they raise. On Flux's
///     side <c>scripts/flux/parity-toast.mjs</c> raises each toast on the live page and measures it; here a
///     shown toast is just markup, so each example is the toast drawn. The script then hands both to
///     <c>parity.mjs</c> under the page name <c>toast-shown</c>, and the section names below are the
///     scenario names it uses.
///     </para>
///     <para>
///     Every toast here stays until dismissed, as the script's do: a timed one would fade out mid-measurement.
///     </para>
/// </remarks>
public sealed partial class ToastShownParity : FluxParity
{
    // The static page has no Rask runtime to honour data-rask-popover-open, so each example shows its own —
    // and takes its entrance to the end, because what Flux's page is measured at is the toast at rest.
    private const string Show =
        "<script>{const toast=document.currentScript.previousElementSibling;toast.showPopover();"
        + "toast.getAnimations({subtree:true}).filter(a=>a.animationName?.startsWith('ui-toast')).forEach(a=>a.finish())}</script>";

    // What Flux's docs pass to their own flux:toast — room above it for the page's navbar, less of it on a wide
    // screen — as an app's own utility under a name of the page's.
    private const string DocsUtilities =
        "<style>.parity-clear{padding-top:7.5rem}@media (min-width:64rem){.parity-clear{padding-top:4rem}}</style>";

    private const string DocsClass = "parity-clear";

    private static readonly RouteUrl Docs = "/components/toast";

    public override string Page => "toast-shown";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("basic", Div[Raw.Value(DocsUtilities), One(Say("Your changes have been saved."))]);
        yield return ("heading", One(Say("You can always update this in your settings.", "Changes saved")));
        yield return ("success", One(Say("The post has been created successfully.", "Post created", ToastLevel.Success)));
        yield return ("warning", One(Say("Your post has unsaved changes.", "Unsaved changes", ToastLevel.Warning)));
        yield return ("danger", One(Say("Your changes have not been saved.", "Something went wrong", ToastLevel.Error)));
        yield return ("invert", One(Say("Your updates are now live.", "Changes saved"), invert: true));
        yield return ("invert-success", One(Say("Your updates are now live.", "Changes saved", ToastLevel.Success), invert: true));
        yield return ("action", One(Say("Your updates are now live.", "Changes saved", ToastLevel.Success) with
        {
            Action = new ToastAction("Undo", default),
        }));
        yield return ("action-link", One(Say("Invoice created.") with
        {
            Action = new ToastAction("View", default) { Href = Docs },
        }));
        yield return ("link", One(Say("Invoice created.", level: ToastLevel.Success) with
        {
            Link = new ToastLink("View invoice", Docs),
        }));

        foreach (var (section, position) in Corners)
        {
            yield return (section, One(Say("Your changes have been saved."), position));
        }

        yield return ("group", Many(expanded: false, Say("Your changes have been saved.")));
        yield return ("group-deck", Many(expanded: false, Four()));
        yield return ("group-open", Many(expanded: true, Four()));
        yield return ("group-expanded", Many(expanded: true, Four()));
    }

    private static readonly (string Section, Ui.ToastPosition Position)[] Corners =
    [
        ("top-end", Ui.ToastPosition.TopEnd),
        ("top-center", Ui.ToastPosition.TopCenter),
        ("top-start", Ui.ToastPosition.TopStart),
        ("bottom-center", Ui.ToastPosition.BottomCenter),
        ("bottom-start", Ui.ToastPosition.BottomStart),
    ];

    private static ToastMessage[] Four() =>
    [
        Say("First, a plain one.") with { Id = 1 },
        Say("With a heading, so it is taller.", "Second", ToastLevel.Success) with { Id = 2 },
        Say("Third, plain again.") with { Id = 3 },
        Say("Fourth.") with { Id = 4 },
    ];

    private static ToastMessage Say(string text, string? heading = null, ToastLevel level = ToastLevel.Info) =>
        new(0, level, text, heading) { Duration = Timeout.InfiniteTimeSpan };

    private static Component One(ToastMessage message, Ui.ToastPosition position = Ui.ToastPosition.BottomEnd, bool invert = false) =>
        Shown(UiToast.Draw([message], static _ => { }, new UiToastLook(position, invert, DocsClass, UiToast.DefaultDuration, null)));

    private static Component Many(bool expanded, params ToastMessage[] messages) =>
        Shown(UiToast.Draw(messages, static _ => { }, new UiToastLook(
            Ui.ToastPosition.BottomEnd, false, null, UiToast.DefaultDuration, new UiToastStack(null, expanded, null))));

    private static Component Shown(Component toast) => Div[toast, Raw.Value(Show)];
}
