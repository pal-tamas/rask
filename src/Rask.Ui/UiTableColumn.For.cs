using System.Linq.Expressions;

namespace Rask;

// The step no property holds: the member of the row the heading names.
public sealed partial class UiTableColumn : ICarriesHandSteps
{
    private UiTableMember? _member;

    /// <summary>
    ///     The heading of the column of a row's member: <c>Ui.TableColumn.For&lt;OrderRead&gt;(o =&gt; o.Total)</c>, with
    ///     <c>Ui.TableCell.For(() =&gt; order.Total)</c> in each row under it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Its words are the member's <c>[Display(Name)]</c>, else its name — a label written in the heading's
    ///         brackets wins. A number or a whole number keeps to the end, as Flux aligns an amount, unless
    ///         <see cref="Align" /> says otherwise. A member that is <c>[ScaffoldColumn(false)]</c> draws no heading, and its
    ///         cells none either. The rest is the heading's own steps: sorting stays the page's —
    ///         <c>.Sortable().Sorted(…).Direction(…).OnSort(…)</c>.
    ///     </para>
    ///     <para>
    ///         Not Flux's: Flux's headings and cells are written by hand. An addition the owner approved (2026-11-24); the
    ///         look is the heading's own.
    ///     </para>
    /// </remarks>
    /// <param name="member">The row's member: <c>r =&gt; r.Plate</c>.</param>
    /// <typeparam name="TRow">The type of a row — a read face, a projection, any class.</typeparam>
    /// <returns>This heading.</returns>
    public UiTableColumn For<TRow>(Expression<Func<TRow, object?>> member)
    {
        _member = UiTableMember.Named(member);
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiTableColumn other)
        {
            other._member = _member ?? other._member;
        }
    }
}
