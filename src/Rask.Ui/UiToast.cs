using Rask.Core.Components;

namespace Rask;

/// <summary>
///     Flux's <c>flux:toast</c>: where the app's toasts appear. Put one in the layout and every
///     <c>Toast.Success("Saved")</c> shows in it.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Toast                                      // bottom end, one at a time
///     Ui.Toast.TopEnd.Invert()                      // another corner, dark on a light page
///     Ui.ToastGroup[Ui.Toast]                       // a stack
///     </code>
///     <para>
///     A toast is raised, not placed: <c>Toast.Success("Saved")</c>, <c>Toast.Error("…").Heading("…")</c>,
///     from any handler. An app that places none gets the host's own — this, in the bottom end corner, moved
///     with <c>c.Toasts</c> — and placing one here replaces it.
///     </para>
///     <para>
///     Without a <see cref="UiToastGroup" /> a new toast takes the place of the one showing. Each goes after
///     its duration, counted in the browser so it waits while the pointer is over it, and by its close button
///     or Escape.
///     </para>
/// </remarks>
public sealed partial class UiToast : Component
{
    /// <summary>Flux's default: a toast shows for five seconds unless it says otherwise.</summary>
    internal static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    /// <summary>Which corner toasts appear in. The bottom end when unset; a group around it decides instead.</summary>
    public Ui.ToastPosition? Position { get; set; }

    /// <summary>Dark in light mode and light in dark, for every toast.</summary>
    public bool? Invert { get; set; }

    /// <summary>
    ///     Classes for the call site, added to each toast's own — <c>pt-24</c> to clear a nav bar at the top.
    /// </summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var stack = Context.Get<UiToastStack>();
        var look = new UiToastLook(
            stack?.Position ?? Position ?? Ui.ToastPosition.BottomEnd, Invert == true, Class, DefaultDuration, stack);

        var outlet = ToastOutlet.Template((messages, dismiss) => Draw(messages, dismiss, look));
        // Each toast carries its own countdown for the browser to run, where it can wait for the pointer.
        outlet.TemplateTimes = true;
        return outlet;
    }
}
