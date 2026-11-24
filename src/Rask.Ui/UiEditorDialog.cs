using Microsoft.Extensions.Primitives;
using Rask.Core.Forms;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wire;

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
///     Ui.EditorDialog.In("edit").Heading(Edit == UiEditorDialog.New ? "New driver" : "Edit driver").Flyout()[
///         Edit == UiEditorDialog.New ? Form.For(Driver.Register)[…] : Form.For(Driver.Change, id)[…]
///     ].Delete(Driver.Delete)
///     </code>
///     <para>
///     A form over an aggregate's operation inside it (<c>Form.For</c>) reports to the dialog: once it saves, the dialog
///     says "Saved" (or the form's own <c>.Saved("Driver saved.")</c>) in a toast and closes, and an id no row has shows "It is not there any more." instead of the form.
///     <c>.Delete(Driver.Delete)</c> — a generated step for an aggregate that can be deleted — adds a Delete button for
///     a stored record whose roles admit the reader (<c>Driver.MayDelete</c>). Pressed, it first tries the delete
///     (<c>Driver.DryRunDelete</c>: its <c>ValidateDelete()</c>) and shows what that refuses instead of asking;
///     otherwise it asks (<c>Ui.Confirm</c>), deletes, says "Deleted" and closes.
///     </para>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-04, #1295), made of <see cref="UiModal" /> — a
///     centred box, or with <see cref="Flyout" /> Flux's flyout from the right, as wide as the screen allows. It reads its
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
public sealed partial class UiEditorDialog : Component, IOperationFormHost
{
    private Func<string, CancellationToken, Task<IReadOnlyList<FieldFailure>>>? _check;
    private Func<string, CancellationToken, Task>? _delete;
    private Func<bool> _mayDelete = static () => false;
    private IReadOnlyList<FieldFailure> _refused = [];
    private string? _refusedFor;

    /// <summary>What the key says for a record not yet saved: <c>?edit=new</c>.</summary>
    public const string New = "new";

    // A small form's box; a form with rows of its own is a sheet from the right, as wide as the screen allows.
    private const string Box = "[:where(&)]:w-full sm:min-w-[28rem]";

    private const string Sheet = "[:where(&)]:w-full md:w-[min(84rem,100vw)]";

    /// <summary>The query key that says which record is open: <c>Ui.EditorDialog.In("edit")</c>. Required, and taken first.</summary>
    /// <remarks>
    ///     Named to the session as the page builds the dialog, before anything renders: a list above it that renders
    ///     first already knows the open dialog's key filters nothing (<c>Ui.Table.Empty</c>).
    /// </remarks>
    public required string In
    {
        get;
        set
        {
            field = value;
            if (UiTableAddress.Route() is { } route)
            {
                UiListAddresses.Of(route).Dialog(value);
            }
        }
    }

    /// <summary>Opens it as a sheet from the right, as wide as the screen allows, for a form with rows of its own.</summary>
    public bool? Flyout { get; set; }

    /// <summary>The dialog's heading, clear of its cross: "Edit driver".</summary>
    public string? Heading { get; set; }

    /// <summary>What the dialog holds — the form — handing the dialog back, so a <c>.Delete(…)</c> can follow it.</summary>
    /// <param name="children">The dialog's content.</param>
    public new UiEditorDialog this[params Component?[] children]
    {
        get
        {
            Children = children;
            return this;
        }
    }

    /// <summary>Classes for the dialog, added to its own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The address opens and closes it, so it renders again whenever the address moves.
        LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();
        var route = UiTableAddress.Route();
        // Its key is no part of where the list stands: the frame's way back to the list leaves it out.
        if (route is not null)
        {
            UiListAddresses.Of(route).Dialog(In);
        }

        var editing = Editing(route);
        if (!string.Equals(editing, _refusedFor, StringComparison.Ordinal))
        {
            _refused = [];
        }

        return Ui.Modal.Key("ui-editor-dialog:" + In).Flyout(Flyout is true).Class(Flyout is true ? Sheet : Box, Class).Open(editing is not null).OnClose(Close)[
            editing is null ? null : Body(editing)
        ];
    }

    /// <inheritdoc />
    Component? IOperationFormHost.Missing =>
        Ui.Callout.Warning.Role("alert").Id("editor-missing").Text(RaskStrings.Get(RaskString.EditorNotFound, "It is not there any more."));

    /// <summary>
    ///     Called by a generated <c>.Delete(Driver.Delete)</c>: how the record the key names is tried and deleted, and
    ///     whether the reader may. Not an API to write against.
    /// </summary>
    /// <param name="check">What the delete of the record would be refused for; empty when nothing.</param>
    /// <param name="delete">Deletes the record.</param>
    /// <param name="may">Whether the signed-in principal may delete it.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void Deleting(
        Func<string, CancellationToken, Task<IReadOnlyList<FieldFailure>>> check, Func<string, CancellationToken, Task> delete, Func<bool> may)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(delete);
        ArgumentNullException.ThrowIfNull(may);
        (_check, _delete, _mayDelete) = (check, delete, may);
    }

    /// <inheritdoc />
    Task IOperationFormHost.Saved(string? message)
    {
        Toast.Success(message ?? RaskStrings.Get(RaskString.EditorSaved, "Saved"));
        Close();
        return Task.CompletedTask;
    }

    // The form reports to the dialog it is in, found in its context.
    private Component Body(string editing) =>
        Context.Provide<IOperationFormHost>(this)[
            Heading is { } heading ? Ui.Heading.Level(2).Lg.Class("mb-6 pe-8")[heading] : null,
            Children is { } content ? Fragment[content] : null,
            Refusals(),
            DeleteButton(editing)
        ];

    private Component? Refusals() =>
        _refused.Count == 0
            ? null
            : Ui.Callout.Danger.Role("alert").Id("editor-delete-refused").Class("mt-6")[
                _refused.Select(static (failure, at) => P.Key(at)[failure.Message])
            ];

    private Component? DeleteButton(string editing) =>
        _check is null || string.Equals(editing, New, StringComparison.Ordinal) || !_mayDelete()
            ? null
            : Div.Class("mt-6 flex")[
                Ui.Button.Danger.Id("editor-delete").OnClick(DeleteAsync)[RaskStrings.Get(RaskString.EditorDelete, "Delete")]
            ];

    private async Task DeleteAsync()
    {
        if (Editing(Navigator.RequireCurrent().Route) is not { } editing || _check is not { } check || _delete is not { } delete)
        {
            return;
        }

        var refused = await check(editing, CancellationToken).ConfigureAwait(false);
        if (refused.Count > 0)
        {
            (_refused, _refusedFor) = (refused, editing);
            return;
        }

        if (!await Ui.Confirm.Ask(RaskStrings.Get(RaskString.EditorDeleteQuestion, "Delete it? This cannot be undone.")).ConfigureAwait(false))
        {
            return;
        }

        await delete(editing, CancellationToken).ConfigureAwait(false);
        Toast.Success(RaskStrings.Get(RaskString.EditorDeleted, "Deleted"));
        Close();
    }

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
