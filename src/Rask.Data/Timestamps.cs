namespace Rask.Data;

/// <summary>
///     Which audit stamps an entity's table carries.
/// </summary>
/// <remarks>
///     <para>
///         Every <see cref="Entity{TId}" /> gets <c>CreatedAt</c> and <c>UpdatedAt</c> by convention. Declare a
///         <c>const</c> named <c>Stamps</c> to narrow that for a table where one of them can never mean
///         anything:
///     </para>
///     <example>
///         <code>
///         public sealed class LogEntry : Entity&lt;long&gt;
///         {
///             public const Timestamps Stamps = Timestamps.Created;   // append-only: nothing updates a log row
///         }
///         </code>
///     </example>
///     <para>
///         Leaving the const off means <see cref="All" />, so an entity that says nothing is unchanged.
///     </para>
///     <para>
///         Read by the generator at COMPILE time and emitted as a registration, never reflected over at run
///         time: a <c>const</c> is inlined at every use site, so the trimmer is free to drop the field — and a
///         reflected read would quietly fall back to <see cref="All" /> in a trimmed publish, which is the
///         kind of silent disagreement between debug and release that is worth going out of the way to avoid.
///     </para>
/// </remarks>
[Flags]
public enum Timestamps
{
    /// <summary>Neither column. For a table that keeps its own notion of time, or none.</summary>
    None = 0,

    /// <summary>
    ///     <c>CreatedAt</c> only — an append-only table, where a row is written once and never changed, so an
    ///     <c>UpdatedAt</c> could only ever repeat it.
    /// </summary>
    Created = 1,

    /// <summary>
    ///     <c>UpdatedAt</c> only — a row whose creation time is already carried by a column of its own that
    ///     means something more specific.
    /// </summary>
    Updated = 2,

    /// <summary>Both. The default when no <c>Stamps</c> const is declared.</summary>
    All = Created | Updated,
}
