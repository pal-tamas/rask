using Rask.Core.Forms;

namespace Rask.Core;

/// <summary>
///     A field's validation rule, sync or async, held as one property.
/// </summary>
/// <remarks>
///     <para>
///         The third carrier family, and it needs to be its own because a validator is the one shape that
///         belongs to neither of the others: like a <see cref="Callback" /> it comes in a sync and an async
///         form that must collapse to one name, and like an <see cref="Fn{TOut}" /> it RETURNS something —
///         the messages that reject the value.
///     </para>
///     <para>
///         <see cref="Invoke" /> hands back a <see cref="ValueTask{TResult}" /> rather than a
///         <see cref="Task{TResult}" /> so the synchronous rule — the overwhelmingly common one, run on
///         every keystroke of every bound control — completes without allocating. That is the same
///         reasoning as <see cref="Callback.Invoke" /> returning <see langword="null" /> for the sync path,
///         expressed the way a value-returning call has to express it.
///     </para>
///     <para>
///         The delegate is stored bare so the edit context keeps registering and dispatching exactly the
///         shape it always did.
///     </para>
/// </remarks>
/// <typeparam name="T">The value being validated.</typeparam>
public readonly struct Validator<T>
{
    private readonly Delegate? _rule;

    /// <summary>Wraps a synchronous rule.</summary>
    public Validator(Validate<T> rule) => _rule = rule;

    /// <summary>
    ///     Wraps an asynchronous rule — a server round-trip, say. Honour the supplied token and let
    ///     <see cref="OperationCanceledException" /> propagate, so a superseded check is dropped rather
    ///     than surfacing a stale message.
    /// </summary>
    public Validator(ValidateAsync<T> rule) => _rule = rule;

    internal Validator(Delegate? rule) => _rule = rule;

    /// <summary>The rule this slot holds. Machinery — validate through <see cref="Invoke" /> instead.</summary>
    /// <remarks>
    ///     Public because the generated setters live in another assembly and the edit context registers the
    ///     bare delegate; hidden from completion because a rule is meant to be run, not read.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Delegate? Rule => _rule;

    /// <summary>Whether a rule was actually supplied.</summary>
    public bool HasValue => _rule is not null;

    /// <summary>
    ///     Runs the rule. An empty sequence means valid; so does an unset slot.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="cancellationToken">Cancelled when a later edit supersedes this check.</param>
    public ValueTask<IEnumerable<string>> Invoke(T value, CancellationToken cancellationToken) => _rule switch
    {
        Validate<T> sync => new ValueTask<IEnumerable<string>>(sync(value)),
        ValidateAsync<T> async => async(value, cancellationToken),
        null => new ValueTask<IEnumerable<string>>([]),
        _ => throw Callback.Unexpected(_rule),
    };
}
