namespace Rask.Core.Forms;

public sealed partial class EditContext
{
    // What made this context async. Without it the sync-validate refusal names the remedy but not the
    // cause, so on a form carrying several validators you find the culprit by bisecting them.
    private string DescribeAsyncValidators()
    {
        var named = new List<string>();
        foreach (var v in _asyncValidators)
        {
            named.Add(v.GetType().Name);
        }

        if (_formDelegate is not null && DelegateValidator.IsAsync(_formDelegate))
        {
            named.Add("an async form-level Validate delegate");
        }

        foreach (var (field, reg) in _fieldDelegates)
        {
            if (DelegateValidator.IsAsync(reg.Validate))
            {
                named.Add($"an async Validate on '{field.FieldName}'");
            }
        }

        return named.Count == 0 ? "none found — this is a framework bug" : string.Join(", ", named);
    }

    /// <summary>
    ///     Validates the whole form and reports whether it passed. Clears the existing messages first, so
    ///     the messages afterwards are exactly this run's.
    /// </summary>
    /// <returns><see langword="true" /> when no field produced a message.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Any registered validator is asynchronous — the result could only be reported by guessing, so
    ///     this refuses rather than return a wrong answer. Use <see cref="ValidateAsync" />. The message
    ///     names which validators made the form async.
    /// </exception>
    public bool Validate()
    {
        if (_asyncValidators.Count > 0 || HasAsyncDelegateValidators)
        {
            throw new InvalidOperationException(
                $"This EditContext has async validators ({DescribeAsyncValidators()}), so it cannot be "
                + "validated synchronously. Call ValidateAsync() instead of Validate().");
        }

        ClearAllMessages();

        // Inline per-field delegates run first, then the form-level inline delegate,
        // then attribute-driven validators (DataAnnotations, FluentValidation, …) in
        // registration order. First-error-wins gates each later stage so a field stays
        // tied to the first rule that flagged it.
        InvokeSyncFieldDelegates();
        InvokeSyncFormDelegate();

        foreach (var v in _validators)
        {
            var pre = SnapshotMessageCounts();
            v.Validate(this);
            TrimGatedMessages(pre);
        }

        ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        return !HasValidationMessages();
    }

    /// <summary>
    ///     Validates one field and reports whether it passed — what a control runs as the user leaves it,
    ///     rather than re-checking the whole form on every keystroke.
    /// </summary>
    /// <param name="field">The field to validate.</param>
    /// <returns><see langword="true" /> when the field produced no message.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Any registered validator is asynchronous. Use <see cref="ValidateFieldAsync" />.
    /// </exception>
    public bool ValidateField(FieldIdentifier field)
    {
        if (_asyncValidators.Count > 0 || HasAsyncDelegateValidators)
        {
            throw new InvalidOperationException(
                $"This EditContext has async validators ({DescribeAsyncValidators()}), so field "
                + $"'{field.FieldName}' cannot be validated synchronously. Call "
                + "ValidateFieldAsync(field) instead of ValidateField(field).");
        }

        ClearMessages(field);

        // Inline field delegate first, then attribute-driven validators — short-circuit
        // as soon as any stage has produced a message for the field (first-error-wins).
        InvokeSyncFieldDelegate(field);

        if (GetValidationMessages(field).Count == 0)
        {
            foreach (var v in _validators)
            {
                v.ValidateField(this, field);
                if (GetValidationMessages(field).Count > 0)
                {
                    break;
                }
            }
        }

        ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        return GetValidationMessages(field).Count == 0;
    }

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

    private void InvokeSyncFieldDelegates()
    {
        foreach (var pair in _fieldDelegates)
        {
            InvokeSyncFieldDelegate(pair.Key, pair.Value);
        }
    }

    private void InvokeSyncFieldDelegate(FieldIdentifier field)
    {
        if (_fieldDelegates.TryGetValue(field, out var reg))
        {
            InvokeSyncFieldDelegate(field, reg);
        }
    }

    private void InvokeSyncFieldDelegate(FieldIdentifier field, DelegateRegistration reg) =>
        RunValidator(field, reg.Validate, reg.ValueGetter);

    private void InvokeSyncFormDelegate()
    {
        if (_formDelegate is not null)
        {
            RunValidator(FormField, _formDelegate, () => Model);
        }
    }

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
