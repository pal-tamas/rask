namespace Rask.Data;

/// <summary>
///     The one child in force of those an aggregate holds: <c>Filings.InForce()</c>,
///     <c>Filings.Put(next, end: f =&gt; f.Unfile(by, at))</c>, <c>Filings.End(f =&gt; f.Unfile(by, at))</c>.
/// </summary>
/// <remarks>
///     Each asks the condition the child declared with <c>b.InForceWhile(…)</c>, the one its unique index is made from.
/// </remarks>
public static class InForceExtensions
{
    /// <summary>The child in force, or <see langword="null" /> when none is.</summary>
    /// <typeparam name="TChild">The child.</typeparam>
    /// <param name="children">The aggregate's children.</param>
    /// <returns>The one in force, or null.</returns>
    /// <exception cref="InvalidOperationException">More than one is — a broken invariant, never ignored.</exception>
    public static TChild? InForce<TChild>(this IEnumerable<TChild> children)
        where TChild : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(children);

        var inForce = InForceRules.For<TChild>();
        TChild? found = null;

        foreach (var child in children)
        {
            if (!inForce(child))
            {
                continue;
            }

            if (found is not null)
            {
                throw new InvalidOperationException(
                    $"More than one '{typeof(TChild).Name}' is in force. End the one in force before another is " +
                    "added — Put(next, end: …) does both.");
            }

            found = child;
        }

        return found;
    }

    /// <summary>Ends the child in force, when there is one.</summary>
    /// <typeparam name="TChild">The child.</typeparam>
    /// <param name="children">The aggregate's children.</param>
    /// <param name="end">What ends it — the child's own method: <c>f =&gt; f.Unfile(by, at)</c>.</param>
    /// <returns>The child that was in force, or null when none was.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="end" /> left it in force.</exception>
    public static TChild? End<TChild>(this IEnumerable<TChild> children, Action<TChild> end)
        where TChild : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(end);

        if (children.InForce() is not { } current)
        {
            return null;
        }

        end(current);
        if (InForceRules.For<TChild>()(current))
        {
            throw new InvalidOperationException(
                $"The '{typeof(TChild).Name}' in force is still in force after it was ended: what ends it must make " +
                "its InForceWhile condition false.");
        }

        return current;
    }

    /// <summary>Puts <paramref name="next" /> in force: ends the child in force, when there is one, then adds it.</summary>
    /// <typeparam name="TChild">The child.</typeparam>
    /// <param name="children">The aggregate's own collection of them.</param>
    /// <param name="next">The child to put in force.</param>
    /// <param name="end">What ends the one in force — the child's own method.</param>
    /// <returns>The child that was in force before, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="next" /> is not in force itself.</exception>
    public static TChild? Put<TChild>(this ICollection<TChild> children, TChild next, Action<TChild> end)
        where TChild : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(next);

        if (!InForceRules.For<TChild>()(next))
        {
            throw new ArgumentException($"The '{typeof(TChild).Name}' to put in force is not in force itself.", nameof(next));
        }

        var ended = children.End(end);
        children.Add(next);
        return ended;
    }
}
