namespace Rask.Core.Forms;

// An asynchronous validator that has nothing to say about most fields. EditContext asks it before treating a field's
// check as asynchronous, so a field it does not cover keeps the synchronous path: no IsValidating, no indicator, no
// await — the form with one async rule does not make every other field look busy.
internal interface IFieldScopedValidator
{
    /// <summary>Whether validating <paramref name="field" /> asks this validator anything.</summary>
    bool Covers(FieldIdentifier field);
}
