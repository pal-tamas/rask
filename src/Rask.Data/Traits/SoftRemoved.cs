namespace Rask.Data;

/// <summary>
///     A child its aggregate lets go of is kept, stamped with <c>DeletedAt</c>, instead of deleted:
///     <c>VirtualTank : Entity&lt;VirtualTankId, SoftRemoved&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///         Removed from its parent's collection — by <c>Sync</c>, <c>Remove</c> or a list's own — the row stays: what
///         points at it still does, and a unique index over it still counts it, so a name it held stays taken. Every
///         read hides it — the aggregate's load and the child's read face — and <c>IgnoreQueryFilters()</c> lists it
///         again.
///     </para>
///     <para>
///         What it holds stays with it. An aggregate root names <see cref="SoftDelete" /> instead.
///     </para>
/// </remarks>
public abstract class SoftRemoved : IEntityTrait;
