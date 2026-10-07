namespace Rask;

/// <summary>
///     Flux's <c>flux:toast.group</c>: wrap <see cref="UiToast" /> in it and toasts stack instead of replacing
///     one another.
/// </summary>
/// <remarks>
///     <code>
///     Ui.ToastGroup[Ui.Toast]                       // a deck that opens under the pointer
///     Ui.ToastGroup.Expanded()[Ui.Toast]            // every toast laid out
///     Ui.ToastGroup.TopEnd[Ui.Toast]                // in another corner
///     </code>
///     <para>
///     Three show at rest, the newest in front and each older one a step back and a little narrower. The
///     pointer over the stack lays them all out.
///     </para>
/// </remarks>
public sealed partial class UiToastGroup : Component
{
    /// <summary>Which corner the stack sits in. The bottom end when unset.</summary>
    public Ui.ToastPosition? Position { get; set; }

    /// <summary>Always lays every toast out, rather than only under the pointer.</summary>
    public bool? Expanded { get; set; }

    /// <summary>Classes for the call site, added to the group's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Context.Provide(new UiToastStack(Position, Expanded == true, Class))[Children ?? []];
}
