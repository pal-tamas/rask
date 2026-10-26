using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     What a filter set binds one of its controls to in place of the page's member: the member as the address
///     holds it, written by moving the address.
/// </summary>
/// <remarks>
///     The router stays the only writer of the page's member, so the page hears a filter change as it hears any
///     move of its query (<c>OnUpdated</c>), and Back and a shared link land on the same filters. The control is
///     still bound, so what it does as it is changed — live, after a pause, on leaving — is its own.
/// </remarks>
/// <typeparam name="T">What the control holds.</typeparam>
internal sealed class UiFilterCell<T>
{
    private readonly ExpressionAccessor.Accessor _member;

    // The set the control is drawn in: a change goes through it, which takes the list back to its first page.
    private UiFilters? _set;

    private UiFilterCell(ExpressionAccessor.Accessor member, string key)
    {
        _member = member;
        Key = key;
        Bind = () => Value!;
    }

    /// <summary>The address key: the member's <c>[QueryParam]</c> name, or its own.</summary>
    internal string Key { get; }

    /// <summary>The bind the control is given instead of the page's.</summary>
    internal Expression<Func<T>> Bind { get; }

    /// <summary>The page's member, as the router last wrote it; written by moving the address.</summary>
    public T? Value
    {
        get => _member.Getter() is T value ? value : default;
        set
        {
            KeyValuePair<string, string?> filter = new(Key, UiFilterAddress.Write(value));
            if (_set is null)
            {
                Go.With(filter.Key, filter.Value);
                return;
            }

            // A setter cannot wait: a pager of buttons told it is on page one goes on as a press on it would.
            _ = _set.Narrow(filter);
        }
    }

    /// <summary>The cell, its changes made through <paramref name="set" />.</summary>
    /// <param name="set">The set the control is drawn in.</param>
    internal UiFilterCell<T> In(UiFilters set)
    {
        _set = set;
        return this;
    }

    /// <summary>The cell a control is bound through: the one it already has, or a new one over the page's member.</summary>
    /// <param name="bind">What the control is bound to now.</param>
    /// <param name="control">The control, as a call site names it, for the message when it is not bound.</param>
    internal static UiFilterCell<T> Over(Expression<Func<T>>? bind, string control)
    {
        if (bind is null)
        {
            throw new InvalidOperationException(
                $"Ui.Filters keeps every filter in the address, so a {control} in it is bound to the page's member that "
                + $"the address fills: {control}.Bind(() => Driver), with [QueryParam] on Driver.");
        }

        var member = ExpressionAccessor.Parse(bind);

        return member.Target as UiFilterCell<T> ?? new UiFilterCell<T>(member, UiFilterAddress.Key(member.Property));
    }

    /// <summary>Whether <paramref name="bind" /> is still the page's, and the control has yet to be handed this cell.</summary>
    internal bool IsNot(Expression<Func<T>>? bind) => !ReferenceEquals(bind, Bind);
}
