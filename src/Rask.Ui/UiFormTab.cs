using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     One tab of a <see cref="UiFormTabs" />: its words, the fields it holds, and what it shows. Its children are
///     the panel: <c>Ui.FormTab.Name("details").Label("Details").Holds(() =&gt; m.Name, () =&gt; m.Email)[ … ]</c>.
/// </summary>
/// <remarks>
///     <para>
///     <see cref="Holds" /> names the fields whose messages the tab counts, by the same member expressions a
///     control binds: <c>() =&gt; m.Name</c> is the field <c>Name</c> on <c>m</c>. A member that is an object or a
///     collection holds every field on what it reaches too — <c>() =&gt; m.Address</c> holds <c>Street</c> and
///     <c>City</c> on that address, <c>() =&gt; m.Rows</c> every field of every row — so the rows of an editor are
///     held by naming their list.
///     </para>
///     <para>
///     A tab that holds nothing (a document, a history) never shows a count, and is never the one a refused save
///     opens.
///     </para>
/// </remarks>
public sealed partial class UiFormTab : Component
{
    private LambdaExpression[] _holds = [];

    /// <summary>
    ///     The tab's name: what ties it to its panel, and what the tab row's value is while it is open. Unique in
    ///     its <see cref="UiFormTabs" />.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>The words on the tab.</summary>
    public required string Label { get; set; }

    /// <summary>Classes for the tab's panel.</summary>
    public string? Class { get; set; }

    /// <summary>
    ///     Names the fields this tab holds, whose messages its badge counts:
    ///     <c>.Holds(() =&gt; m.Number, () =&gt; m.CountryId)</c>. An object or a collection holds every field it
    ///     reaches (<c>.Holds(() =&gt; m.Pistols)</c>: each pistol's fields).
    /// </summary>
    /// <param name="fields">Member expressions on the form's model, or on an object inside it.</param>
    /// <returns>This tab.</returns>
    public UiFormTab Holds(params Expression<Func<object?>>[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        // Replaced, never added to: the page's next render builds the chain again on this same tab.
        _holds = fields;
        // What a generated step does when it writes a new value: the tab is drawn again with it.
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    // The panel is drawn by the tab itself, in the group its UiFormTabs opened; the row above it is the
    // UiFormTabs' to draw, from the tab's Name, Label and what it holds.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render() => Ui.TabPanel.Name(Name).Class(Class)[Children ?? []];

    /// <summary>
    ///     The fields this tab holds on <paramref name="form" />: each named member, and every field on the objects its
    ///     value reaches.
    /// </summary>
    internal UiFormTabFields FieldsOn(EditContext form)
    {
        var fields = new HashSet<FieldIdentifier>();
        var owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var held in _holds)
        {
            var accessor = ExpressionAccessor.Parse(held);
            fields.Add(accessor.Field);
            if (accessor.Getter() is { } value && !ModelGraphWalker.IsLeaf(value.GetType()))
            {
                owners.UnionWith(ModelGraphWalker.Walk(value));
            }
        }

        // The form's own model is no owner a tab holds: every field would be its.
        owners.Remove(form.Model);
        return new UiFormTabFields(fields, owners);
    }
}
