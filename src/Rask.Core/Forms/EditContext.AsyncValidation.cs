namespace Rask.Core.Forms;

public sealed partial class EditContext
{
    /// <summary>
    ///     Validates the whole form, running every rule — synchronous and asynchronous — and reports whether
    ///     it passed. Clears the existing messages first, so the messages afterwards are exactly this run's.
    /// </summary>
    /// <param name="cancellationToken">Cancels the in-flight validators.</param>
    /// <returns><see langword="true" /> when no field produced a message.</returns>
    public async ValueTask<bool> Validate(CancellationToken cancellationToken = default)
    {
        // With no token of its own a submit stops with the work it runs in: the form going away, a timeout.
        cancellationToken = Ambient.Or(cancellationToken);

        // Supersede every in-flight per-field run before we re-validate from scratch.
#pragma warning disable S6966 // cancel synchronously: a superseded run must see it before this call returns
        foreach (var s in _states.Values)
        {
            s.Cts?.Cancel();
        }
#pragma warning restore S6966

        ClearAllMessages();

        // Inline per-field delegates first: sync invoked, async awaited. Each one isolates
        // its own exception so one bad delegate doesn't kill the whole submit pipeline.
        foreach (var pair in _fieldDelegates)
        {
            await InvokeFieldDelegateAsync(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Form-level inline delegate runs next so cross-field rules (e.g. "passwords match")
        // can observe the per-field messages just produced above.
        if (_formDelegate is not null)
        {
            await InvokeFormDelegateAsync(_formDelegate, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Attribute-driven validators (DataAnnotations, FluentValidation, …) follow,
        // in registration order — sync first, then async. First-error-wins gates each
        // validator so a field that's already flagged stays tied to the earliest rule.
        foreach (var v in _validators)
        {
            var pre = SnapshotMessageCounts();
            v.Validate(this);
            TrimGatedMessages(pre);
        }

        await RunAsyncValidatorsAsync(cancellationToken).ConfigureAwait(false);

        ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        return !HasValidationMessages();
    }

    private async ValueTask RunAsyncValidatorsAsync(CancellationToken cancellationToken)
    {
        foreach (var v in _asyncValidators)
        {
            var pre = SnapshotMessageCounts();
            try
            {
                await v.Validate(this, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                AddValidationMessage(FormField, CouldNotValidate);
            }

            TrimGatedMessages(pre);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    ///     Validates one field, awaiting any asynchronous rule, and reports whether it passed. While it
    ///     runs, <see cref="IsValidating" /> reports the field as in flight, which is what a pending
    ///     indicator watches.
    /// </summary>
    /// <param name="field">The field to validate.</param>
    /// <param name="cancellationToken">Cancels the in-flight validator.</param>
    /// <returns><see langword="true" /> when the field produced no message.</returns>
    public async ValueTask<bool> ValidateField(FieldIdentifier field,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreate(field);

        // A value the reader has committed — a change, a blur, the pause of a debounced field — unless a
        // live field said it is still being typed. A store's rules are asked about a committed value only.
        var committed = !IsTypedInto(field);

        // Latest-wins: cancel any prior in-flight run for this field.
        // Note on the CTS lifecycle (looks racy, isn't): the live transports serialize handler
        // execution end to end — the Server WS dispatcher and the WASM session each hold their
        // lock across the whole awaited handler (which is where validation runs), so two
        // ValidateField calls for the same field never overlap. By the time a later call
        // reaches here, the earlier one has already nulled state.Cts (sync + finally paths), so
        // these Cancel/Dispose calls only ever touch a still-owned CTS — no double-dispose, no
        // ObjectDisposedException. Keep validation off background threads to preserve this.
#pragma warning disable S6966 // cancel synchronously: a superseded run must see it before this call returns
        state.Cts?.Cancel();
#pragma warning restore S6966
        state.Cts?.Dispose();
        // Linked to the work this runs in when the caller hands no token: a handler's is its control's
        // lifetime, so a check in flight stops when the field goes away.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(Ambient.Or(cancellationToken));
        state.Cts = cts;

        ClearRuleMessages(field);

        var hasFieldDelegate = _fieldDelegates.TryGetValue(field, out var fieldReg);
        var fieldDelegateIsAsync = hasFieldDelegate && DelegateValidator.IsAsync(fieldReg.Validate);

        // Fast sync path: nothing async to await. Inline first, then attribute-driven, and
        // first-error-wins short-circuits as soon as any stage flags the field.
        if (!fieldDelegateIsAsync && !AwaitsValidators(committed))
        {
            if (hasFieldDelegate)
            {
                InvokeSyncFieldDelegate(field, fieldReg);
            }

            RunSyncFieldValidators(field, state);
            ValidationStateChanged?.Invoke(this, EventArgs.Empty);
            if (ReferenceEquals(state.Cts, cts))
            {
                state.Cts = null;
            }

            cts.Dispose();
            return state.Messages.Count == 0;
        }

        return await ValidateFieldPendingAsync(
            field, state, fieldReg, hasFieldDelegate, fieldDelegateIsAsync, committed, cts).ConfigureAwait(false);
    }

    // A value still being typed is not put to the store, so a keystroke with nothing else to await stays synchronous.
    private bool AwaitsValidators(bool committed)
    {
        var awaited = _asyncValidators.Count;
        if (!committed && awaited > 0 && _asyncValidators[^1] is StoreRuleValidator)
        {
            awaited--;
        }

        return awaited > 0;
    }

    private async ValueTask<bool> ValidateFieldPendingAsync(
        FieldIdentifier field, FieldState state, DelegateRegistration fieldReg, bool hasFieldDelegate,
        bool fieldDelegateIsAsync, bool committed, CancellationTokenSource cts)
    {
        // Async path. Enter the pending bookkeeping up front so the inline delegate (async
        // or sync) runs ahead of the attribute-driven validators — same order as the sync
        // path above.
        var wasZero = state.PendingCount == 0;
        state.PendingCount++;
        if (wasZero)
        {
            ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            if (fieldDelegateIsAsync)
            {
                if (!await RunAsyncFieldDelegateAsync(field, fieldReg, cts).ConfigureAwait(false))
                {
                    return false;
                }
            }
            else if (hasFieldDelegate)
            {
                InvokeSyncFieldDelegate(field, fieldReg);
            }

            RunSyncFieldValidators(field, state);
            if (state.Messages.Count == 0 && !await RunAsyncFieldValidatorsAsync(field, state, committed, cts).ConfigureAwait(false))
            {
                return false;
            }

            return state.Messages.Count == 0;
        }
        finally
        {
            state.PendingCount--;
            if (state.PendingCount == 0)
            {
                if (ReferenceEquals(state.Cts, cts))
                {
                    state.Cts = null;
                }

                ArmStickyDismissal(field, state);
                ValidationStateChanged?.Invoke(this, EventArgs.Empty);
            }

            cts.Dispose();
        }
    }

    // False when the run was superseded or cancelled; a validator that throws is reported as a message instead.
    private async ValueTask<bool> RunAsyncFieldDelegateAsync(
        FieldIdentifier field, DelegateRegistration fieldReg, CancellationTokenSource cts)
    {
        try
        {
            var msgs = await DelegateValidator.InvokeAsync(
                fieldReg.Validate, fieldReg.ValueGetter(), cts.Token).ConfigureAwait(false);
            foreach (var m in msgs)
            {
                AddValidationMessage(field, m);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            AddValidationMessage(field, CouldNotValidate);
        }

        return !cts.IsCancellationRequested;
    }

    // As RunAsyncFieldDelegateAsync, over the registered async validators, stopping at the first message.
    private async ValueTask<bool> RunAsyncFieldValidatorsAsync(
        FieldIdentifier field, FieldState state, bool committed, CancellationTokenSource cts)
    {
        foreach (var v in _asyncValidators)
        {
            if (!committed && v is StoreRuleValidator)
            {
                continue;
            }

            try
            {
                await v.ValidateField(this, field, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                AddValidationMessage(field, CouldNotValidate);
            }

            if (cts.IsCancellationRequested)
            {
                return false;
            }

            if (state.Messages.Count > 0)
            {
                break;
            }
        }

        return true;
    }
}
