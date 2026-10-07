using System.Text;

namespace Rask;

/// <summary>
///     One card of a <see cref="UiKanban" />: Flux's <c>flux:kanban.card</c>.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.KanbanCard.Heading("Update privacy policy in app")</c> is something to read. With
///     <c>.As(Ui.KanbanCardAs.Button)</c> the whole card is a <c>&lt;button&gt;</c> — focused with Tab, pressed
///     with Enter or Space, lighter under the pointer — and <c>.OnClick(…)</c> is what it does.
///     </para>
///     <para>
///     <see cref="Header" /> sits above the heading (badges, tags) and <see cref="Footer" /> under it (an icon,
///     avatars). Children take the place of <see cref="Heading" />.
///     </para>
///     <para>
///     Flux marks the card it draws as a button <c>data-flux-kanban-card</c> and the one it draws as a div
///     <c>flux-kanban-card</c>, without the <c>data-</c>; the kit writes <c>data-ui-kanban-card</c> and
///     <c>ui-kanban-card</c> the same way.
///     </para>
/// </remarks>
public sealed partial class UiKanbanCard : UiElement, IUiHost
{
    private const string Look = "p-3 rounded-lg bg-white shadow-xs ring-1 ring-black/7 dark:bg-zinc-700 dark:ring-zinc-700";

    // A <button> where Flux scripts a custom element: the block it fills, its words from the start, and no
    // text selection or hand cursor, as there.
    private const string Pressed =
        "block w-full text-start cursor-default select-none hover:bg-zinc-50 dark:hover:bg-zinc-700 dark:hover:ring-zinc-600";

    private const string HeaderSlot = "flex items-center gap-1.5 mb-2";

    private const string FooterSlot = "flex items-center gap-1.5 mt-2";

    private static readonly UiPartMarker Marker = new("ui-kanban-card");

    /// <summary>The card's title.</summary>
    public string? Heading { get; set; }

    /// <summary>The element it is. Unset, <see cref="Ui.KanbanCardAs.Div" />.</summary>
    public Ui.KanbanCardAs? As { get; set; }

    /// <summary>What sits above the heading — badges, tags: Flux's <c>header</c> slot.</summary>
    /// <remarks>Hides the inherited <c>Header</c> tag entry inside this component; <c>Markup.Header</c> still reaches the tag.</remarks>
    public new Component? Header { get; set; }

    /// <summary>What sits under the heading — an icon, avatars: Flux's <c>footer</c> slot.</summary>
    /// <remarks>Hides the inherited <c>Footer</c> tag entry inside this component; <c>Markup.Footer</c> still reaches the tag.</remarks>
    public new Component? Footer { get; set; }

    /// <inheritdoc />
    /// <remarks>None: it renders the card with its header and footer around what it says.</remarks>
    protected override string? TagName => null;

    private bool IsButton => As == Ui.KanbanCardAs.Button;

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(Look, IsButton ? Pressed : "", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?>? ResolveData() => IsButton ? Marker.With(Data) : Data;

    /// <inheritdoc />
    protected override Component? Render() =>
        HostedElement.Tag(IsButton ? "button" : "div").Owner(this)[
            Header is null ? null : Div.Class(HeaderSlot)[Header],
            Children?.Any() == true ? Children : Title(),
            Footer is null ? null : Div.Class(FooterSlot)[Footer]
        ];

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        if (IsButton)
        {
            // Without it a card inside a form would submit the form.
            AppendAttr(sb, "type", "button");
        }
        else
        {
            AppendAttr(sb, "ui-kanban-card", null);
        }
    }

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);

    private IEnumerable<Component?> Title() => [Heading is null ? null : Ui.Heading[Heading]];
}
