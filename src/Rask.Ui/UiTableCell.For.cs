using System.Linq.Expressions;

namespace Rask;

// The step no property holds: the member of the row the cell shows.
public sealed partial class UiTableCell : ICarriesHandSteps
{
    private UiTableMember? _member;
    private string? _shown;

    /// <summary>
    ///     A cell showing a row's member, written for the reader: <c>Ui.TableCell.For(() =&gt; order.Total)</c>, under a
    ///     <c>Ui.TableColumn.For&lt;OrderRead&gt;(o =&gt; o.Total)</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The value is written in the reader's culture by the member's <c>[DisplayFormat(DataFormatString)]</c>, else
    ///         by what it holds: a number with its value object's scale, a day, a moment on the wall clock, an enum's
    ///         <c>[Display(Name)]</c>, a <c>bool</c> as Yes or No (<see cref="RaskString.TableCellYes" />,
    ///         <see cref="RaskString.TableCellNo" />), a value object as its value. No value is the member's
    ///         <c>[DisplayFormat(NullDisplayText)]</c>, else "–". A number keeps to the end, as its heading does; a
    ///         <c>[ScaffoldColumn(false)]</c> member draws no cell. Content in the cell's brackets wins over the value.
    ///     </para>
    ///     <para>Not Flux's: an addition the owner approved (2026-11-24); the look is the cell's own.</para>
    /// </remarks>
    /// <param name="member">The row's member: <c>() =&gt; order.Total</c>.</param>
    /// <typeparam name="T">The member's type.</typeparam>
    /// <returns>This cell.</returns>
    public UiTableCell For<T>(Expression<Func<T>> member)
    {
        var (bound, value) = UiTableMember.Bound(member);
        _member = bound;
        _shown = bound.Text(value);
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiTableCell other)
        {
            other._member = _member ?? other._member;
            other._shown = _shown ?? other._shown;
        }
    }
}
