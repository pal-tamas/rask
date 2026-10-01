namespace Rask.Core.Forms;

public sealed partial class EditContext
{
    // First-error-wins: skip sync IFieldValidators once the field already has a message from an earlier stage.
    private void RunSyncFieldValidators(FieldIdentifier field, FieldState state)
    {
        if (state.Messages.Count > 0)
        {
            return;
        }

        foreach (var v in _validators)
        {
            v.ValidateField(this, field);
            if (state.Messages.Count > 0)
            {
                return;
            }
        }
    }

    // First-error-wins gating. We snapshot the per-field message count before invoking a
    // validator, then trim any messages that validator added on fields that already had
    // earlier messages. Two consequences:
    //   * Across stages — inline → form-level → sync IFieldValidator → async IAsyncFieldValidator
    //     — a field can only carry an error from the *first* stage that produced one.
    //   * Across validators within the sync or async stage, the earliest registered validator
    //     to flag a field wins; later validators don't pile on for the same field.
    // When the upstream rule passes on a re-validate, the snapshot is empty and the next
    // stage gets to run, which gives the "fix the error, the next rule kicks in" behaviour.
    private Dictionary<FieldIdentifier, int> SnapshotMessageCounts()
    {
        var snapshot = new Dictionary<FieldIdentifier, int>();
        foreach (var pair in _states)
        {
            if (pair.Value.Messages.Count > 0)
            {
                snapshot[pair.Key] = pair.Value.Messages.Count;
            }
        }

        return snapshot;
    }

    private void TrimGatedMessages(Dictionary<FieldIdentifier, int> preCounts)
    {
        foreach (var pair in preCounts)
        {
            if (_states.TryGetValue(pair.Key, out var s) && s.Messages.Count > pair.Value)
            {
                s.Messages.RemoveRange(pair.Value, s.Messages.Count - pair.Value);
            }
        }
    }

    private void InvokeSyncFieldDelegate(FieldIdentifier field, DelegateRegistration reg) =>
        RunValidator(field, reg.Validate, reg.ValueGetter);

    private ValueTask InvokeFieldDelegateAsync(
        FieldIdentifier field, DelegateRegistration reg, CancellationToken cancellationToken) =>
        RunValidatorAsync(field, reg.Validate, reg.ValueGetter, cancellationToken);

    private ValueTask InvokeFormDelegateAsync(Delegate validate, CancellationToken cancellationToken) =>
        RunValidatorAsync(FormField, validate, () => Model, cancellationToken);

    // One inline Validate delegate, run over the value it validates, its messages recorded against field. A
    // validator that throws — reading the value included — is one generic message, never a crashed form.
    private void RunValidator(FieldIdentifier field, Delegate validate, Func<object?> value)
    {
        try
        {
            foreach (var msg in DelegateValidator.InvokeSync(validate, value()))
            {
                AddValidationMessage(field, msg);
            }
        }
        catch
        {
            AddValidationMessage(field, CouldNotValidate);
        }
    }

    // The same for a delegate that may be asynchronous; a sync one runs as above. Cancellation is not a
    // validation failure, so it propagates to the caller that cancelled.
    private async ValueTask RunValidatorAsync(
        FieldIdentifier field, Delegate validate, Func<object?> value, CancellationToken cancellationToken)
    {
        if (!DelegateValidator.IsAsync(validate))
        {
            RunValidator(field, validate, value);
            return;
        }

        try
        {
            var msgs = await DelegateValidator.InvokeAsync(validate, value(), cancellationToken).ConfigureAwait(false);
            foreach (var m in msgs)
            {
                AddValidationMessage(field, m);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            AddValidationMessage(field, CouldNotValidate);
        }
    }
}
