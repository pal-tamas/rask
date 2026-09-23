namespace Rask.Data;

/// <summary>
///     Whether a save of this entity is announced to the whole process, so that pages which did not make the write
///     can refresh themselves.
/// </summary>
/// <remarks>
///     <para>
///         <b>Off is the default.</b> A save already refreshes the saving session's own screen — that is
///         <see cref="IDataChanges" />, and it costs nothing to declare. Declare a <c>const</c> named
///         <c>Broadcast</c> to reach everyone else's too:
///     </para>
///     <example>
///         <code>
///         public sealed class Order : Aggregate&lt;Guid&gt;
///         {
///             public const Broadcasts Broadcast = Broadcasts.OnCommit;
///         }
///         </code>
///     </example>
///     <para>
///         A query opts in on its side with <c>[Live(typeof(Order))]</c>, or by being keyed
///         <c>QueryKey.For&lt;Order&gt;(…)</c>, so an entity that announces itself
///         costs nothing until something is listening. Announcing happens after the save commits, never on a
///         rollback, and — unlike <see cref="IDataChanges" /> — it does not need a session, so a background job's
///         write reaches open pages too.
///     </para>
/// </remarks>
public enum Broadcasts
{
    /// <summary>A save is told to this session only. The default.</summary>
    Never = 0,

    /// <summary>A save is announced to the whole process once it commits.</summary>
    OnCommit = 1,
}
