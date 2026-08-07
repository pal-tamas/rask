namespace Rask.Core;

/// <summary>
///     PROTOTYPE — the runtime the generated builder setters call into (see
///     <c>RaskBuilderSetters*.g.cs</c>).
/// </summary>
/// <remarks>
///     Public because the setters are emitted into the GLOBAL namespace of every consuming assembly —
///     an extension method is only found when its namespace is in scope, and the global namespace
///     encloses all — so they cannot reach an internal member of Rask.Core.
/// </remarks>
public static class BuilderRuntime
{
    /// <summary>
    ///     Records a prop change on <paramref name="target" /> when the setter's value differs from the
    ///     one already there, so the deferred commit reports <c>propsChanged: true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the setter-chain half of what the generated factory does in one shot: snapshot
    ///         every folding prop, assign them all, then <c>NotifyParameters(__c, __propsChanged)</c>.
    ///         A chain has no natural end, so the "did anything change" fold is accumulated here as it
    ///         happens and the notification is deferred to the end of the parent's <c>Render()</c>.
    ///     </para>
    ///     <para>
    ///         <see cref="EqualityComparer{T}" /> matches the factory exactly: reference equality for a
    ///         reference type that does not override <c>Equals</c>, structural for primitives. The
    ///         factory's exclusions are honoured by the generator instead of here — <c>Key</c> (a
    ///         reconciliation identity, not a reactive prop), auto-wrapped callbacks and raw delegates
    ///         (a fresh closure every render, so folding them would force a change every frame) and
    ///         carrier props simply get a setter with no <c>Track</c> call.
    ///     </para>
    /// </remarks>
    public static void Track<TValue>(Component target, TValue oldValue, TValue newValue)
    {
        if (!EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
        {
            target.MarkEntryPropsChangedInternal();
        }
    }
}
