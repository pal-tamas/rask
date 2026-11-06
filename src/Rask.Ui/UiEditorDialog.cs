using Microsoft.Extensions.Primitives;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     The dialog a list's record is added or changed in, over the list and at the list's own address: the query key it
///     is <see cref="In" /> says which record is open (<see cref="New" /> for one not yet saved), so a link, a reload or
///     Back opens and closes it.
/// </summary>
/// <remarks>
///     <code>
///     [QueryParam("edit")] public string? Edit { get; set; }
///
///     Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New driver"]
///     Ui.RowActions.Edit(route.With("edit", driver.Id.ToString(CultureInfo.InvariantCulture)))
///     Ui.EditorDialog.In("edit").Heading(Edit == UiEditorDialog.New ? "New driver" : "Edit driver").Wide()[
///         DriverEditing.Key(Edit).Editing(Edit)
///     ]
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-04, #1295), made of <see cref="UiModal" /> — a
///     centred box, or with <see cref="Wide" /> Flux's flyout from the right, as wide as the screen allows. It reads its
///     key from the address as it renders and holds its content only while the key is there. The cross, Escape and a
///     press outside take the key out (<c>Go.Without(key)</c>, in the current entry's place), so the list stands as the
///     reader left it — its filters, sort and page kept; Back, after the link that opened it, closes it the same way.
///     A save ends with <c>Go.Without("edit")</c>, and the list, rendering again, reads its rows again.
///     </para>
///     <para>
///     An editor's old address of its own keeps working when its page sends the reader on:
///     <c>Go.To(Routes.DriversPage().With("edit", id)).Replacing()</c> in its <c>OnMount</c>.
///     </para>
/// </remarks>
public sealed partial class UiEditorDialog : Component
{
    /// <summary>What the key says for a record not yet saved: <c>?edit=new</c>.</summary>
    public const string New = "new";

    // A small form's box; a form with rows of its own is a sheet from the right, as wide as the screen allows.
    private const string Box = "[:where(&)]:w-full sm:min-w-[28rem]";

    private const string Sheet = "[:where(&)]:w-full md:w-[min(84rem,100vw)]";

    /// <summary>The query key that says which record is open: <c>Ui.EditorDialog.In("edit")</c>. Required, and taken first.</summary>
    public required string In { get; set; }

    /// <summary>Opens it as a sheet from the right, as wide as the screen allows, for a form with rows of its own.</summary>
    public bool? Wide { get; set; }

    /// <summary>The dialog's heading, clear of its cross: "Edit driver".</summary>
    public string? Heading { get; set; }

    /// <summary>Classes for the dialog, added to its own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The address opens and closes it, so it renders again whenever the address moves.
        LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();
        var open = Editing(UiTableAddress.Route()) is not null;

        return Ui.Modal.Key("ui-editor-dialog:" + In).Flyout(Wide is true).Class(Wide is true ? Sheet : Box, Class).Open(open).OnClose(Close)[
            open ? Body() : null
        ];
    }

    private Component Body() =>
        Fragment[
            Heading is { } heading ? Ui.Heading.Level(2).Lg.Class("mb-6 pe-8")[heading] : null,
            Children is { } content ? Fragment[content] : null
        ];

    // Closed by the reader — the cross, Escape, a press outside — the key goes, unless Back already took it.
    private void Close()
    {
        if (Editing(Navigator.RequireCurrent().Route) is not null)
        {
            Go.Without(In);
        }
    }

    private string? Editing(RouteState? route) =>
        route is not null && route.Query.TryGetValue(In, out var value) && !StringValues.IsNullOrEmpty(value) ? value.ToString() : null;
}
