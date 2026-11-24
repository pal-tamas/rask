namespace Rask.Data;

/// <summary>What a <see cref="Children{TEntity,TSpec}.Sync" /> changed.</summary>
/// <remarks>
///     <code>
/// if (Chips.Sync(chips, c =&gt; c.Id, Chip.For, (c, s) =&gt; c.Hold(s)) is { Touched.Count: &gt; 0 } sync)
///     Raise(new ChipsChanged(sync.Touched));
///     </code>
///     <para>
///         <see cref="Touched" /> is what a rule over old rows asks: a row stored before the rule was tightened is let
///         be, unless this change wrote it.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The child.</typeparam>
public sealed class SyncResult<TEntity>
{
    internal SyncResult(IReadOnlyList<TEntity> added, IReadOnlyList<TEntity> changed, IReadOnlyList<TEntity> removed, IReadOnlyList<TEntity> touched) =>
        (Added, Changed, Removed, Touched) = (added, changed, removed, touched);

    /// <summary>The children made from a spec that named none, in the specs' order.</summary>
    public IReadOnlyList<TEntity> Added { get; }

    /// <summary>The children whose update changed a property, in the specs' order; one its spec left as it was is not here.</summary>
    public IReadOnlyList<TEntity> Changed { get; }

    /// <summary>The children no spec named, let go.</summary>
    public IReadOnlyList<TEntity> Removed { get; }

    /// <summary>The children added or changed, in the specs' order.</summary>
    public IReadOnlyList<TEntity> Touched { get; }
}
