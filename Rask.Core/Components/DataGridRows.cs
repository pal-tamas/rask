using Rask.Core.DataGrids;

namespace Rask.Core.Components;

// Iterates the visible rows from the ambient DataGridContext<TRow> and emits one rendered
// fragment per row via the user-supplied Row template. Required to be a descendant of a
// DataGrid<TRow> with matching TRow.
public sealed class DataGridRows<TRow> : Component
{
    public required Func<TRow, Component> Row { get; set; }

    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var ctx = DataGridScope.CurrentAs<TRow>();
        if (ctx is null)
        {
            return new Fragment();
        }

        var children = new List<Child>();
        foreach (var row in ctx.VisibleRows)
        {
            children.Add(Row(row));
        }

        return new Fragment(children);
    }
}
