using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The steps of a declared lock (<see cref="LockedByBuilderExtensions" />):
///     <c>b.LockedBy(…).Except(t =&gt; t.RefundId).Including(p =&gt; p.Refund)</c>.
/// </summary>
/// <typeparam name="T">The row that is locked.</typeparam>
/// <typeparam name="TParent">The row whose state locks it.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LockedByBuilder<T, [DynamicallyAccessedMembers(DataTrimming.Entity)] TParent>
    where T : class
    where TParent : class
{
    private readonly ParentLock<T, TParent> _lock;

    internal LockedByBuilder(ParentLock<T, TParent> rule) => _lock = rule;

    /// <summary>
    ///     Lets a change that touches only <paramref name="columns" /> through, under a locked parent too — a claim taking a
    ///     transaction over by its refund column. An add, a delete, and a change to any other column are still refused.
    /// </summary>
    /// <param name="columns">The row's properties: <c>t =&gt; t.RefundId</c>.</param>
    /// <returns>The same steps.</returns>
    /// <exception cref="ArgumentException">A column names no property of the row directly.</exception>
    public LockedByBuilder<T, TParent> Except(params Expression<Func<T, object?>>[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        foreach (var column in columns)
        {
            _lock.Excepted.Add(PropertyExpressions.Single(column, nameof(columns)));
        }

        return this;
    }

    /// <summary>Loads <paramref name="navigation" /> of each locked parent, for the message to read: <c>p =&gt; p.Refund</c>.</summary>
    /// <typeparam name="TProperty">The navigation's type.</typeparam>
    /// <param name="navigation">The parent's navigation.</param>
    /// <returns>The same steps.</returns>
    public LockedByBuilder<T, TParent> Including<TProperty>(Expression<Func<TParent, TProperty>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _lock.Include(query => query.Include(navigation));
        return this;
    }
}
