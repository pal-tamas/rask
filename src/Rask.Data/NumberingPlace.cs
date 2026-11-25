using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     Where a numbered row's values in memory place it — its group and the first member of its order — with each
///     computed column (<see cref="ComputedColumnBuilderExtensions.Computed{TEntity, TProperty}" />) read as what it is
///     computed from, so a row about to be added or moved is placed where the database is going to put it, not where the
///     value it held before the save says.
/// </summary>
/// <typeparam name="TEntity">The numbered entity.</typeparam>
internal sealed class NumberingPlace<TEntity>
    where TEntity : class
{
    private readonly Func<TEntity, object?> _within;
    private readonly Func<TEntity, object?> _first;

    /// <summary>Reads <paramref name="within" /> and <paramref name="first" />, their computed members inlined.</summary>
    internal NumberingPlace(LambdaExpression within, LambdaExpression first)
    {
        var inlinedWithin = ComputedValues.Inlined(within);
        var inlinedFirst = ComputedValues.Inlined(first);
        Computed = inlinedWithin != within || inlinedFirst != first;
        _within = Read(inlinedWithin);
        _first = Read(inlinedFirst);
    }

    /// <summary>Whether a computed member decides the place: what the row held before the save may not be where it is.</summary>
    internal bool Computed { get; }

    /// <summary>Reads a lambda of the row, boxed.</summary>
    internal static Func<TEntity, object?> Read(LambdaExpression lambda) =>
        Expression.Lambda<Func<TEntity, object?>>(Expression.Convert(lambda.Body, typeof(object)), lambda.Parameters[0]).Compile();

    /// <summary>
    ///     The row's place from its values in memory; false when a computed member cannot be computed in C# from them
    ///     (a division by zero, a value object not there) and only the database's value read back after the write says it.
    /// </summary>
    internal bool TryRead(TEntity row, out object? within, out object? first)
    {
        try
        {
            within = _within(row);
            first = _first(row);
            return true;
        }
        catch (Exception computing) when (Computed && computing is ArithmeticException or NullReferenceException or InvalidOperationException)
        {
            within = null;
            first = null;
            return false;
        }
    }
}
