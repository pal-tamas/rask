namespace Rask.Data;

/// <summary>
///     A deleted row is kept, stamped with <c>DeletedAt</c>, and every read hides it; <c>IgnoreQueryFilters()</c> lists it
///     again.
/// </summary>
/// <remarks>
///     <para>
///         On an aggregate root it is the generated <c>X.Delete(id)</c>: the stamp instead of the delete.
///     </para>
///     <para>
///         On a child — <c>VirtualTank : Entity&lt;VirtualTankId&gt;, ISoftDeletes</c> — it is what its aggregate lets go
///         of: removed from its parent's collection by <c>Sync</c>, <c>Remove</c> or a list's own, the row stays. What
///         points at it still does, and a unique index over it still counts it, so a name it held stays taken. Every
///         read hides it — the aggregate's load and the child's read face — and what it holds stays with it.
///     </para>
/// </remarks>
#pragma warning disable CA1040 // a marker: the interface IS the declaration, read by the generator
public interface ISoftDeletes;
#pragma warning restore CA1040
