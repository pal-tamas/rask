using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A numbering <see cref="NumberedBuilderExtensions.Numbered{TEntity, TNumber, TWithin, TOrder}" /> declared, and what
///     more decides it: a row that takes another's number (<see cref="Same{TRow}" />), a row another table's rows leave out
///     (<see cref="SkipWhen{TOther, TRow}" />).
/// </summary>
/// <typeparam name="TEntity">The numbered entity.</typeparam>
public sealed partial class Numbering<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>
    where TEntity : class
{
    private readonly List<Follower> _followers = [];
    private readonly HashSet<string> _members = new(StringComparer.Ordinal);
    private readonly List<LambdaExpression> _read = [];
    private readonly Lazy<NumberingPlace<TEntity>> _now;
    private readonly Func<IQueryable<TEntity>, object?, CancellationToken, Task<int>> _write;
    private readonly Func<TEntity, object?> _readWithin;
    private readonly Func<TEntity, object?> _readFirst;

    internal Numbering(
        string number,
        LambdaExpression numberLambda,
        LambdaExpression within,
        IReadOnlyList<LambdaExpression> order,
        LambdaExpression? skip,
        Func<IQueryable<TEntity>, object?, CancellationToken, Task<int>> write)
    {
        NumberName = number;
        NumberLambda = numberLambda;
        Within = within;
        Order = order;
        First = NumberingOrder.Placed(order[0]);
        Skip = skip;
        _write = write;
        _readWithin = NumberingPlace<TEntity>.Read(within);
        _readFirst = NumberingPlace<TEntity>.Read(First);
        _now = new Lazy<NumberingPlace<TEntity>>(Now);
        foreach (var lambda in (IEnumerable<LambdaExpression?>)[within, .. order, skip])
        {
            Notice(lambda);
        }
    }

    /// <inheritdoc />
    string INumbering.NumberName => NumberName;

    internal string NumberName { get; }

    /// <inheritdoc />
    string INumbering.Name => Name;

    internal string Name => typeof(TEntity).Name + "." + NumberName;

    private LambdaExpression NumberLambda { get; }

    private LambdaExpression Within { get; }

    private IReadOnlyList<LambdaExpression> Order { get; }

    // The order's first member as a row's place is compared by (NumberingOrder.Placed); the order sorts by Order[0].
    private LambdaExpression First { get; }

    private LambdaExpression? Skip { get; set; }

    private SameRows? SameAs { get; set; }

    /// <summary>
    ///     A row that <paramref name="row" /> names takes that row's number instead of a place of its own: an IBC tank's
    ///     part has its fill's — <c>.Same(f =&gt; f.ParentId)</c>.
    /// </summary>
    /// <typeparam name="TRow">The type of the reference, the key's own or its nullable.</typeparam>
    /// <param name="row">The key of the row whose number this one takes, or null for a row of its own place.</param>
    /// <returns>The same numbering.</returns>
    public Numbering<TEntity> Same<TRow>(Expression<Func<TEntity, TRow>> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Notice(row);
        if (row.ReturnType.IsValueType && Nullable.GetUnderlyingType(row.ReturnType) is null)
        {
            throw new ArgumentException($"Same({row}) names the row whose number this one takes, or null for none: it must be nullable.", nameof(row));
        }

        SameAs = new SameRows(row, NumberingPlace<TEntity>.Read(row), (rows, keys) =>
        {
            var wanted = keys.Select(static key => (TRow)key).ToList();
            Expression<Func<TRow, bool>> named = value => wanted.Contains(value);
            var probe = NumberingOrder.Rebind.To(named, row.Body);
            return rows.Where(Expression.Lambda<Func<TEntity, bool>>(probe, row.Parameters[0]));
        });
        return this;
    }

    /// <summary>
    ///     Leaves out a row when another table's rows say so — a fueling distributed from is not counted:
    ///     <c>.SkipWhen&lt;Distribution, string&gt;((f, shares) =&gt; shares.Any(s =&gt; s.FromFuelingId == f.Id), s =&gt; s.FromFuelingId)</c>.
    /// </summary>
    /// <typeparam name="TOther">The other table's entity.</typeparam>
    /// <typeparam name="TRow">The type of its reference to the numbered row.</typeparam>
    /// <param name="when">Whether the row is left out, asked of the other table.</param>
    /// <param name="row">The numbered row an <typeparamref name="TOther" /> row speaks of: a save that adds, changes or deletes one numbers that row's group anew.</param>
    /// <returns>The same numbering.</returns>
    public Numbering<TEntity> SkipWhen<[DynamicallyAccessedMembers(DataTrimming.Entity)] TOther, TRow>(
        Expression<Func<TEntity, IQueryable<TOther>, bool>> when, Expression<Func<TOther, TRow>> row)
        where TOther : class
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(row);
        Notice(when);

        var compiled = row.Compile();
        _followers.Add(new Follower(
            typeof(TOther),
            context => Expression.Lambda(
                new NumberingOrder.Rebind(when.Parameters[1], context.Set<TOther>().AsQueryable().Expression).Visit(when.Body),
                when.Parameters[0]),
            entity => compiled((TOther)entity)));
        return this;
    }

    // The row's own members a lambda reads: a change to one of them can move the row.
    private void Notice(LambdaExpression? lambda)
    {
        if (lambda is null)
        {
            return;
        }

        _read.Add(lambda);
        new Members(lambda.Parameters[0], _members).Visit(lambda.Body);
    }

    // Once the model is built, so every Computed(…) is declared whichever came first: the members a computed column is
    // computed from move the row too, and the place in memory reads that column as computed from them.
    private NumberingPlace<TEntity> Now()
    {
        foreach (var lambda in _read.ToList())
        {
            new Members(lambda.Parameters[0], _members).Visit(ComputedValues.Inlined(lambda).Body);
        }

        return new NumberingPlace<TEntity>(Within, First);
    }

    /// <summary>The other table whose rows leave numbered rows out, and how to read which one.</summary>
    private sealed record Follower(Type Other, Func<DbContext, LambdaExpression> Skips, Func<object, object?> RowOf);

    /// <summary>How a row names the one whose number it takes, and the query for those that name some of a set.</summary>
    private sealed record SameRows(
        LambdaExpression Row, Func<TEntity, object?> Of, Func<IQueryable<TEntity>, IEnumerable<object>, IQueryable<TEntity>> Naming);

    private sealed class Members(ParameterExpression row, HashSet<string> names) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == row)
            {
                names.Add(node.Member.Name);
            }

            return base.VisitMember(node);
        }
    }
}
