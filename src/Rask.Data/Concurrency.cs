namespace Rask.Data;

/// <summary>
///     Whether an aggregate carries the <c>Version</c> token that refuses a write to a row somebody else
///     changed.
/// </summary>
/// <remarks>
///     <para>
///         <b>On is the default.</b> Declare a <c>const</c> named <c>Checks</c> to go without:
///     </para>
///     <example>
///         <code>
///         public sealed class Reading : Aggregate&lt;Guid&gt;
///         {
///             public const Concurrency Checks = Concurrency.None;   // append-only telemetry, never edited twice
///         }
///         </code>
///     </example>
///     <para>
///         It stays on where <see cref="Deletion" /> was turned off, and the asymmetry is deliberate. A lost
///         delete is VISIBLE — the row is gone and somebody notices. A lost update is not: two people open the
///         same form, both are told "Saved", and the second silently erases the first. Nobody ever finds out.
///     </para>
///     <para>
///         The cost that would normally argue for turning it off — an unhandled
///         <c>DbUpdateConcurrencyException</c> in a user's face — Rask already absorbs: the scaffolded edit
///         page catches it and says that somebody else changed the row. The framework pays for this default,
///         so an application gets the check for free.
///     </para>
/// </remarks>
public enum Concurrency
{
    /// <summary>No <c>Version</c> column, and no check. The last write wins.</summary>
    None,

    /// <summary>
    ///     A <c>Version</c> column, bumped on every save and compared on every update. The default.
    /// </summary>
    Version,
}
