namespace Rask;

/// <summary>
/// Flux UI's <c>flux:command</c>: a searchable list of commands — a <see cref="UiCommandInput" /> over
/// <see cref="UiCommandItems" />.
/// </summary>
/// <remarks>
/// <para>
/// <code>
/// Ui.Command[
///     Ui.CommandInput.Placeholder("Search..."),
///     Ui.CommandItems[
///         Ui.CommandItem.Icon(Ui.IconName.UserPlus).Kbd("⌘A").OnClick(Assign)["Assign to…"],
///         Ui.CommandItem.Icon(Ui.IconName.DocumentPlus).OnClick(Create)["Create new file"]
///     ]
/// ]
/// </code>
/// Typing narrows the list, ArrowUp and ArrowDown move the lit command, Enter presses it: all in the browser
/// (<c>data-rask-command</c>, <c>rask-command.ts</c>), so nothing is sent until a command is pressed — which is the
/// page's own <see cref="UiCommandItem.OnClick" />. As a palette, it sits in a bare <see cref="UiModal" /> opened by
/// a <see cref="UiModalTrigger" />, as Flux's "As a modal" example does.
/// </para>
/// <para>
/// Flux caps the palette at 480px only while it is handed no class (measured): <see cref="Class" /> takes the cap
/// away, as the modal example's classes do.
/// </para>
/// </remarks>
public sealed partial class UiCommand : Component
{
    private static readonly UiPartMarker Marker = new("ui-command");

    private const string Look = "block w-full overflow-hidden rounded-xl border border-zinc-200 shadow-xs dark:border-zinc-600";

    /// <summary>Classes for the palette: a width, the modal example's <c>border-none</c>.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(Look, Class is null ? "max-w-120" : null, Class)
            .Data(Marker.With(null))
            .Attributes(("data-rask-command", null))[Children ?? []];
}
