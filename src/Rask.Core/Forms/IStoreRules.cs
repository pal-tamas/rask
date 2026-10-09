using Rask.Wire;

namespace Rask.Core.Forms;

/// <summary>
///     The rules a store keeps for one form model — a unique index, a range that must not overlap — asked
///     while the form is being filled in, so a value the store would refuse is told before the save.
/// </summary>
/// <remarks>
///     A data layer implements it and announces it with <see cref="RaskValidation.RegisterStoreRules" />.
///     A form asks as each field is committed and once more on submit, after its own rules have passed. It
///     does not ask twice for a field whose value has not changed and no other field has, so an
///     implementation need not remember its own answers, though one asked about two fields of the same rule
///     in a row may want to.
/// </remarks>
public interface IStoreRules
{
    /// <summary>What the store would refuse about the model as it is now.</summary>
    /// <param name="model">The form's model, holding the values to check.</param>
    /// <param name="field">
    ///     The field that was just committed, named as the model names it — <c>Name</c>,
    ///     <c>Price.Amount</c>, <c>Lines[2].ValidFrom</c> — and only the rules that read it need checking.
    ///     <see langword="null" /> on submit: every rule.
    /// </param>
    /// <param name="cancellationToken">
    ///     Cancelled when a later commit of the same field supersedes this one, or the form goes away.
    /// </param>
    /// <returns>
    ///     One failure per rule the values break, each naming the fields its message is shown under; empty
    ///     when the store would accept them. A rule whose fields are not all filled in yet is not a failure.
    /// </returns>
    ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken);
}
