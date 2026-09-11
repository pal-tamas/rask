namespace Rask.Ui;

/// <summary>
/// A list of rows, styled by the kit. It IS the <c>&lt;ul&gt;</c> — or the <c>&lt;ol&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="UiElement" />, so <c>Id</c>, <c>Class</c>, <c>Data</c>, <c>Role</c>, <c>Aria</c> and the
/// events all apply with nothing redeclared, and the kit's classes compose with a call site's through
/// <see cref="ResolveClass" />.
/// </para>
/// <para>
/// <see cref="Ordered" /> picks the tag rather than a second component, because the difference is
/// meaning, not styling: in an <c>&lt;ol&gt;</c> the sequence carries information and a screen reader says
/// so — a log, a set of steps, a ranking. It also turns the numbers on, since the reset every Tailwind app
/// ships (<c>ol, ul { list-style: none }</c>) would otherwise draw an ordered list with no ordinals.
/// </para>
/// <para>
/// A row is a plain <c>Li</c>. The list pads and divides its own direct children, so a one-line entry needs
/// no component of its own; a row with a picture, text that should take the remaining width and actions
/// beside it is <see cref="UiListRow" />, whose padding is daisyUI's and is left alone here.
/// </para>
/// <para>
/// The row padding is a rule in the kit's stylesheet on the <c>ui-list</c> marker, below the app's utilities,
/// so a row that writes its own — <c>ps-2</c> beside a number, <c>px-0</c> flush with a heading — gets it. A
/// <c>[&amp;&gt;li]:px-4</c> variant here would be the more specific selector and silently win instead.
/// </para>
/// </remarks>
public sealed partial class UiList : UiElement
{
    /// <summary>
    ///     Renders an <c>&lt;ol&gt;</c> and numbers the rows. For a sequence whose order carries meaning.
    /// </summary>
    public bool? Ordered { get; set; }

    /// <inheritdoc />
    protected override string TagName => Ordered == true ? "ol" : "ul";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "ui-list list overflow-hidden rounded-box border border-base-300 bg-base-100 divide-y divide-base-300",
            Ordered == true ? "list-decimal list-inside" : "",
            Class);
}
