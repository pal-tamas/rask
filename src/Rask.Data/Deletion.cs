namespace Rask.Data;

/// <summary>
///     What <c>DeleteAsync</c> does to an aggregate's row.
/// </summary>
/// <remarks>
///     <para>
///         <b>Hard is the default.</b> Declare a <c>const</c> named <c>Deletes</c> to keep the row instead:
///     </para>
///     <example>
///         <code>
///         public sealed class Order : Aggregate&lt;Guid&gt;
///         {
///             public const Deletion Deletes = Deletion.Soft;
///         }
///         </code>
///     </example>
///     <para>
///         Soft delete used to be the default, and is not any more, for three reasons that cost an ordinary
///         application more than the recovery was worth. A stamped row still occupies its UNIQUE constraints,
///         so deleting the account <c>a@b.com</c> and letting that person sign up again fails on a row nobody
///         can see. "Delete my account" has to be able to mean delete. And Rask already ships real recovery —
///         SQLite snapshots and Litestream — so keeping the row was solving that problem a second time, worse,
///         while charging every query a predicate.
///     </para>
///     <para>
///         <see cref="None" /> takes the delete away altogether, for an aggregate that is cancelled or archived
///         rather than removed:
///     </para>
///     <example>
///         <code>
///         public sealed class Invoice : Aggregate&lt;Guid&gt;
///         {
///             public const Deletion Deletes = Deletion.None;   // Invoice.DeleteAsync does not exist
///         }
///         </code>
///     </example>
///     <para>
///         The one to weigh it against is <see cref="Concurrency" />, which stays ON by default: a lost delete
///         is visible, and a lost update is not.
///     </para>
/// </remarks>
public enum Deletion
{
    /// <summary>The row is removed. The default.</summary>
    Hard,

    /// <summary>
    ///     The row is stamped with <c>DeletedAt</c> and hidden by a global query filter, which
    ///     <c>IgnoreQueryFilters()</c> lifts.
    /// </summary>
    Soft,

    /// <summary>
    ///     The aggregate is never deleted: no <c>DeleteAsync</c> is generated for it. For a record the domain
    ///     corrects by adding another (a refund, a reversal) or retires through a method of its own
    ///     (<c>Cancel</c>, <c>Archive</c>) — an invoice, a payment, a ledger entry.
    /// </summary>
    None,
}
