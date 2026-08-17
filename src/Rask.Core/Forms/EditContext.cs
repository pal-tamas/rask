using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

public sealed class EditContext : IDisposable
{
    // Default sticky window for the ValidatingIndicator. After PendingCount
    // drops to 0, the field stays "validating" for this many milliseconds —
    // smooths over very-short async checks (100-400ms validators) that would
    // otherwise leave a DOM footprint too brief for screen-readers and for
    // load-balanced Playwright polling to reliably observe. Per-instance via
    // <see cref="ValidatingStickyMs" />; set to 0 to opt out.
    public const int DefaultValidatingStickyMs = 200;

    private readonly List<IAsyncFieldValidator> _asyncValidators = new();
    private readonly Dictionary<FieldIdentifier, DelegateRegistration> _fieldDelegates = new();
    private readonly Dictionary<FieldIdentifier, FieldState> _states = new();
    private readonly List<IFieldValidator> _validators = new();

    // The component that authored each field's bind expression (recorded when the control registers its
    // validator). A two-way write re-renders this consumer so derived UI it owns — even a sibling of the
    // Form, outside the control's own re-render scope — refreshes with no StateHasChanged. Mirrors the
    // controlled-mode AutoCallback owner-rerender, for bound mode.
    private readonly Dictionary<FieldIdentifier, Component> _bindingOwners = new();
    private Delegate? _formDelegate;

    public EditContext(object model) => Model = model ?? throw new ArgumentNullException(nameof(model));

    /// <summary>
    ///     Override the sticky window for this context. Default 200 ms.
    ///     Set to 0 to disable (the indicator disappears immediately when
    ///     PendingCount drops to 0 — the pre-sticky behaviour).
    /// </summary>
    public int ValidatingStickyMs { get; set; } = DefaultValidatingStickyMs;

    public object Model { get; }

    internal IEnumerable<FieldIdentifier> RegisteredFields => _states.Keys;

    public bool HasAsyncValidators => _asyncValidators.Count > 0 || HasAsyncDelegateValidators;

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

    public bool HasAsyncDelegateValidators
    {
        get
        {
            if (_formDelegate is not null && DelegateValidator.IsAsync(_formDelegate))
            {
                return true;
            }

            foreach (var reg in _fieldDelegates.Values)
            {
                if (DelegateValidator.IsAsync(reg.Validate))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool IsValidatingAny
    {
        get
        {
            MarkReader();
            foreach (var s in _states.Values)
            {
                if (s.PendingCount > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    ///     Optional fire-and-forget render-request callback wired by the
    ///     framework (LiveRenderContext) when this context is attached to a
    ///     live render. Currently invoked by the sticky-dismissal timer so the
    ///     UI re-renders to drop the ValidatingIndicator when the sticky tail
    ///     expires — without this hook the indicator would only disappear on
    ///     the next unrelated render. Null on unit-test contexts; sticky still
    ///     functions correctly there (IsValidating + sticky-tail observation),
    ///     it just won't proactively re-render on its own.
    /// </summary>
    internal Action? RequestRender { get; set; }

    // True once Dispose has released this context's per-field timers/CTS. Set by the live render
    // when the backing form unmounts. Purely diagnostic — nothing gates behaviour on it, and a
    // disposed context re-arms its resources cleanly if the same instance is re-mounted.
    internal bool IsDisposed { get; private set; }

    /// <summary>
    ///     Releases the per-field background resources this context owns: the one-shot
    ///     sticky-dismissal <see cref="System.Threading.Timer" />s and any in-flight
    ///     async-validation <see cref="CancellationTokenSource" />. The live render calls
    ///     this when the form backing the context is unmounted, so a sticky timer that
    ///     would otherwise outlive the form (default 200&#160;ms tail) can neither fire a
    ///     stale render nor pin the context graph alive. Nulling <see cref="RequestRender" />
    ///     additionally makes any timer callback already in flight no-op its render request.
    ///     Idempotent — <see cref="System.Threading.Timer" /> / <see cref="CancellationTokenSource" />
    ///     disposal is safe to repeat.
    /// </summary>
    public void Dispose()
    {
        IsDisposed = true;
        RequestRender = null;
        foreach (var s in _states.Values)
        {
            s.StickyTimer?.Dispose();
            s.StickyTimer = null;
            s.Cts?.Dispose();
            s.Cts = null;
        }
    }

    public event Action<FieldIdentifier>? FieldChanged;
    public event Action? ValidationStateChanged;

    public void AddValidator(IFieldValidator validator)
    {
        if (validator is null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        var t = validator.GetType();
        if (_validators.Any(v => v.GetType() == t))
        {
            return;
        }

        _validators.Add(validator);
    }

    public void AddValidator(IAsyncFieldValidator validator)
    {
        if (validator is null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        var t = validator.GetType();
        if (_asyncValidators.Any(v => v.GetType() == t))
        {
            return;
        }

        _asyncValidators.Add(validator);
    }

    // Per-field inline Validate delegate from Input/Select/Textarea factories. Passing
    // `validate: null` clears any prior registration so a re-render that drops the parameter
    // doesn't leave a stale callback in place. The value getter lets the dispatcher read the
    // current field value without reflecting on every validate call.
    public void RegisterFieldValidator(FieldIdentifier field, Delegate? validate, Func<object?> valueGetter)
    {
        if (validate is null)
        {
            _fieldDelegates.Remove(field);
            return;
        }

        _fieldDelegates[field] = new DelegateRegistration(validate, valueGetter);
    }

    // Convenience overload for callers that don't have a getter handy (tests, direct API
    // use). Uses reflection over the model's runtime type to resolve the value.
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the model's runtime type — same constraint as DataAnnotations: " +
                        "the user-owned model's public properties are preserved by their binding setup.")]
    public void RegisterFieldValidator(FieldIdentifier field, Delegate? validate) =>
        RegisterFieldValidator(field, validate, () =>
            field.Model.GetType().GetProperty(field.FieldName)?.GetValue(field.Model));

    // Form-level inline Validate delegate. Null clears.
    public void RegisterFormValidator(Delegate? validate) => _formDelegate = validate;

    // Latches the component currently mid-Render() as reading untracked EditContext state, so it
    // permanently opts out of the render cache (Component.RenderForLive) and re-executes Render() to
    // observe later validation-message / validating-state changes. Exactly the mechanism Context.Get
    // uses (Context.MarkConsumer). CurrentSync is non-null only during an active live render, so calls
    // from the validation/submit pipeline are no-ops — no over-marking, no allocation on the hot path.
    private static void MarkReader() => Live.LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();

    public bool IsValidating(FieldIdentifier field)
    {
        MarkReader();
        return _states.TryGetValue(field, out var s) && s.PendingCount > 0;
    }

    /// <summary>
    ///     <see cref="IsValidating(FieldIdentifier)" /> extended with a short
    ///     sticky tail (<see cref="ValidatingStickyMs" />, default 200 ms): a
    ///     validator that finishes inside the sticky window still reads as
    ///     "showing" so the <c>ValidatingIndicator</c>
    ///     gives screen-readers and Playwright a reliably observable footprint
    ///     for sub-second async checks. The dismissal is a single timer-driven
    ///     re-render at window expiry — see ArmStickyDismissal.
    ///     <para>
    ///         Use <see cref="IsValidating" /> for control-flow decisions
    ///         (submit gating, message clearing) where you want the exact
    ///         "no validator currently in flight" answer.
    ///     </para>
    /// </summary>
    public bool ShouldShowValidatingIndicator(FieldIdentifier field)
    {
        MarkReader();
        if (!_states.TryGetValue(field, out var s))
        {
            return false;
        }

        if (s.PendingCount > 0)
        {
            return true;
        }

        return s.StickyUntilUtc is { } until && until > DateTimeOffset.UtcNow;
    }

    public bool IsModified(FieldIdentifier field)
    {
        MarkReader();
        return _states.TryGetValue(field, out var s) && s.Modified;
    }

    public bool IsTouched(FieldIdentifier field)
    {
        MarkReader();
        return _states.TryGetValue(field, out var s) && s.Touched;
    }

    public IReadOnlyList<string> GetValidationMessages(FieldIdentifier field)
    {
        MarkReader();
        return _states.TryGetValue(field, out var s) ? s.Messages : Array.Empty<string>();
    }

    public IEnumerable<string> GetValidationMessages()
    {
        // Mark before returning the iterator: MarkReader() inside the yield body would only run on
        // first MoveNext (deferred), missing a render that enumerates lazily or not at all.
        MarkReader();
        return Enumerate();

        IEnumerable<string> Enumerate()
        {
            foreach (var s in _states.Values)
                foreach (var m in s.Messages)
                {
                    yield return m;
                }
        }
    }

    public IReadOnlyList<ValidationEntry> GetValidationEntries()
    {
        MarkReader();
        var entries = new List<ValidationEntry>();
        foreach (var pair in _states)
            foreach (var m in pair.Value.Messages)
            {
                entries.Add(new ValidationEntry(pair.Key.FieldName, m));
            }

        return entries;
    }

    public bool HasValidationMessages()
    {
        MarkReader();
        return _states.Values.Any(s => s.Messages.Count > 0);
    }

    // Records the consumer that owns a field's bind expression so a write can re-render it. Idempotent per
    // render; null owners (bindings closed over a non-component root) are ignored.
    internal void TrackBindingOwner(FieldIdentifier field, Component? owner)
    {
        if (owner is not null)
        {
            _bindingOwners[field] = owner;
        }
    }

    public void NotifyFieldChanged(FieldIdentifier field)
    {
        var s = GetOrCreate(field);
        s.Modified = true;
        FieldChanged?.Invoke(field);

        // Re-render the binding's authoring component so its derived UI (including siblings outside the
        // Form / the control) reflects the new model value — the bound-mode counterpart of the
        // controlled-OnChange consumer re-render. The control already re-renders itself; this covers the host.
        if (_bindingOwners.TryGetValue(field, out var owner))
        {
            owner.StateHasChanged();
        }
    }

    public void NotifyFieldTouched(FieldIdentifier field) =>
        GetOrCreate(field).Touched = true;

    public void ClearMessages(FieldIdentifier field)
    {
        if (_states.TryGetValue(field, out var s) && s.Messages.Count > 0)
        {
            s.Messages.Clear();
            ValidationStateChanged?.Invoke();
        }
    }

    public void ClearAllMessages()
    {
        var any = false;
        foreach (var s in _states.Values)
        {
            if (s.Messages.Count > 0)
            {
                s.Messages.Clear();
                any = true;
            }
        }

        if (any)
        {
            ValidationStateChanged?.Invoke();
        }
    }

    // Idempotent on (field, message): a second identical add is a no-op — neither the
    // list nor the event observe it. Matches ASP.NET Core ModelStateDictionary's
    // duplicate-error suppression and closes the door on the "same message rendered twice"
    // class of bugs that can otherwise arise when a validator runs against the same field
    // through more than one path (full Validate + per-field re-validate, re-entrant Render
    // on a sync-context resume, etc.).
    public void AddValidationMessage(FieldIdentifier field, string message)
    {
        var state = GetOrCreate(field);
        if (state.Messages.Contains(message, StringComparer.Ordinal))
        {
            return;
        }

        state.Messages.Add(message);
        ValidationStateChanged?.Invoke();
    }

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

        ValidationStateChanged?.Invoke();
        return !HasValidationMessages();
    }

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

        ValidationStateChanged?.Invoke();
        return GetValidationMessages(field).Count == 0;
    }

    public async ValueTask<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        // Supersede every in-flight per-field run before we re-validate from scratch.
        foreach (var s in _states.Values)
        {
            s.Cts?.Cancel();
        }

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

        if (_asyncValidators.Count > 0)
        {
            foreach (var v in _asyncValidators)
            {
                var pre = SnapshotMessageCounts();
                try
                {
                    await v.ValidateAsync(this, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    AddValidationMessage(new FieldIdentifier(Model, string.Empty),
                        "Validation could not be completed.");
                }

                TrimGatedMessages(pre);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        ValidationStateChanged?.Invoke();
        return !HasValidationMessages();
    }

    public async ValueTask<bool> ValidateFieldAsync(FieldIdentifier field,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreate(field);

        // Latest-wins: cancel any prior in-flight run for this field.
        // Note on the CTS lifecycle (looks racy, isn't): the live transports serialize handler
        // execution end to end — the Server WS dispatcher and the WASM session each hold their
        // lock across the whole awaited handler (which is where validation runs), so two
        // ValidateFieldAsync calls for the same field never overlap. By the time a later call
        // reaches here, the earlier one has already nulled state.Cts (sync + finally paths), so
        // these Cancel/Dispose calls only ever touch a still-owned CTS — no double-dispose, no
        // ObjectDisposedException. Keep validation off background threads to preserve this.
        state.Cts?.Cancel();
        state.Cts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        state.Cts = cts;

        ClearMessages(field);

        var hasFieldDelegate = _fieldDelegates.TryGetValue(field, out var fieldReg);
        var fieldDelegateIsAsync = hasFieldDelegate && DelegateValidator.IsAsync(fieldReg.Validate);

        // Fast sync path: nothing async to await. Inline first, then attribute-driven;
        // first-error-wins short-circuits as soon as any stage flags the field.
        if (!fieldDelegateIsAsync && _asyncValidators.Count == 0)
        {
            if (hasFieldDelegate)
            {
                InvokeSyncFieldDelegate(field, fieldReg);
            }

            if (state.Messages.Count == 0)
            {
                foreach (var v in _validators)
                {
                    v.ValidateField(this, field);
                    if (state.Messages.Count > 0)
                    {
                        break;
                    }
                }
            }

            ValidationStateChanged?.Invoke();
            if (ReferenceEquals(state.Cts, cts))
            {
                state.Cts = null;
            }

            cts.Dispose();
            return state.Messages.Count == 0;
        }

        // Async path. Enter the pending bookkeeping up front so the inline delegate (async
        // or sync) runs ahead of the attribute-driven validators — same order as the sync
        // path above.
        var wasZero = state.PendingCount == 0;
        state.PendingCount++;
        if (wasZero)
        {
            ValidationStateChanged?.Invoke();
        }

        try
        {
            if (hasFieldDelegate)
            {
                if (fieldDelegateIsAsync)
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
                        AddValidationMessage(field, "Validation could not be completed.");
                    }

                    if (cts.IsCancellationRequested)
                    {
                        return false;
                    }
                }
                else
                {
                    InvokeSyncFieldDelegate(field, fieldReg);
                }
            }

            // First-error-wins: skip sync IFieldValidators once the field already has a
            // message from an earlier stage.
            if (state.Messages.Count == 0)
            {
                foreach (var v in _validators)
                {
                    v.ValidateField(this, field);
                    if (state.Messages.Count > 0)
                    {
                        break;
                    }
                }
            }

            if (state.Messages.Count == 0)
            {
                foreach (var v in _asyncValidators)
                {
                    try
                    {
                        await v.ValidateFieldAsync(this, field, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (Exception)
                    {
                        AddValidationMessage(field, "Validation could not be completed.");
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
                ValidationStateChanged?.Invoke();
            }

            cts.Dispose();
        }
    }

    // Stamps the sticky deadline and schedules a one-shot dismissal render so the
    // ValidatingIndicator gets removed promptly when the sticky tail expires
    // (without this timer the IsValidating(field) flip from true to false would
    // only land on the next unrelated render). The render is requested via
    // <see cref="ValidationStateChanged" /> — LiveRenderContext wires that event
    // to the root component's render handle when the EditContext is first
    // attached to a live render.
    private void ArmStickyDismissal(FieldIdentifier field, FieldState state)
    {
        var sticky = ValidatingStickyMs;
        if (sticky <= 0)
        {
            state.StickyUntilUtc = null;
            return;
        }

        state.StickyUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(sticky);
        state.StickyTimer?.Dispose();
        state.StickyTimer = new Timer(static s =>
        {
            var (ctx, fid) = ((EditContext, FieldIdentifier))s!;
            if (!ctx._states.TryGetValue(fid, out var inner))
            {
                return;
            }

            // Only clear if we're still in the same sticky window: a fresh
            // PendingCount > 0 in the meantime resets StickyUntilUtc and the
            // new cycle owns the dismissal.
            if (inner.PendingCount > 0)
            {
                return;
            }

            inner.StickyUntilUtc = null;
            ctx.ValidationStateChanged?.Invoke();
            // Drive the sticky-dismissal render through the host so the
            // indicator actually leaves the DOM. ValidationStateChanged is a
            // user-facing notification; the render request itself goes via the
            // injected callback that LiveRenderContext wires on attach.
            ctx.RequestRender?.Invoke();
        }, (this, field), sticky, Timeout.Infinite);
    }

    public void TouchAllRegisteredFields()
    {
        foreach (var s in _states.Values)
        {
            s.Touched = true;
        }

        // Field delegates may target fields that haven't been touched by a binding yet;
        // make sure their messages survive the next per-keystroke gate.
        foreach (var field in _fieldDelegates.Keys)
        {
            GetOrCreate(field).Touched = true;
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

    internal FieldState GetOrCreate(FieldIdentifier field)
    {
        if (!_states.TryGetValue(field, out var s))
        {
            s = new FieldState();
            _states[field] = s;
        }

        return s;
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

    private void InvokeSyncFieldDelegate(FieldIdentifier field, DelegateRegistration reg)
    {
        try
        {
            foreach (var msg in DelegateValidator.InvokeSync(reg.Validate, reg.ValueGetter()))
            {
                AddValidationMessage(field, msg);
            }
        }
        catch
        {
            AddValidationMessage(field, "Validation could not be completed.");
        }
    }

    private void InvokeSyncFormDelegate()
    {
        if (_formDelegate is null)
        {
            return;
        }

        try
        {
            foreach (var msg in DelegateValidator.InvokeSync(_formDelegate, Model))
            {
                AddValidationMessage(new FieldIdentifier(Model, string.Empty), msg);
            }
        }
        catch
        {
            AddValidationMessage(new FieldIdentifier(Model, string.Empty), "Validation could not be completed.");
        }
    }

    private async ValueTask InvokeFieldDelegateAsync(
        FieldIdentifier field, DelegateRegistration reg, CancellationToken cancellationToken)
    {
        if (DelegateValidator.IsAsync(reg.Validate))
        {
            try
            {
                var msgs = await DelegateValidator.InvokeAsync(
                    reg.Validate, reg.ValueGetter(), cancellationToken).ConfigureAwait(false);
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
                AddValidationMessage(field, "Validation could not be completed.");
            }
        }
        else
        {
            InvokeSyncFieldDelegate(field, reg);
        }
    }

    private async ValueTask InvokeFormDelegateAsync(Delegate validate, CancellationToken cancellationToken)
    {
        var formField = new FieldIdentifier(Model, string.Empty);
        if (DelegateValidator.IsAsync(validate))
        {
            try
            {
                var msgs = await DelegateValidator.InvokeAsync(validate, Model, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var m in msgs)
                {
                    AddValidationMessage(formField, m);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                AddValidationMessage(formField, "Validation could not be completed.");
            }
        }
        else
        {
            InvokeSyncFormDelegate();
        }
    }

    internal sealed class FieldState
    {
        public CancellationTokenSource? Cts;
        public List<string> Messages = new();
        public bool Modified;
        public int PendingCount;

        public Timer? StickyTimer;

        // Sticky window. When PendingCount drops to 0 the EditContext stamps
        // a UTC deadline here so IsValidating(field) keeps returning true for
        // a short tail after the validator finishes — gives a 400ms async
        // check a visible footprint screen-readers and Playwright polling
        // can reliably observe. StickyTimer schedules the dismissal render
        // and gets disposed when the next PendingCount > 0 starts or when
        // the field is no longer alive.
        public DateTimeOffset? StickyUntilUtc;
        public bool Touched;
    }

    private readonly struct DelegateRegistration
    {
        public DelegateRegistration(Delegate validate, Func<object?> valueGetter)
        {
            Validate = validate;
            ValueGetter = valueGetter;
        }

        public Delegate Validate { get; }
        public Func<object?> ValueGetter { get; }
    }
}
