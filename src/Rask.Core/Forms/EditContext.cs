using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

/// <summary>
///     The validation state of one form: which fields the user has touched or modified, what is wrong with
///     them, and which checks are still running. A <c>Form</c> creates and owns one, so most apps never
///     construct it — reach for it when you need to ask about validity outside a control, or drive
///     validation yourself.
/// </summary>
/// <remarks>
///     Fields are identified by <see cref="FieldIdentifier" />, which is an object reference plus a
///     property name — so a field on a nested sub-object is distinct from a same-named field on the root,
///     and no string path has to be assembled.
///     <para>
///         Whatever this says, validate again on the server. Client-side validation exists to tell the
///         user what is wrong before they submit, not to keep bad data out.
///     </para>
/// </remarks>
public sealed partial class EditContext : IDisposable
{
    // Default sticky window for the Validation.Indicator. After PendingCount
    // drops to 0, the field stays "validating" for this many milliseconds —
    // smooths over very-short async checks (100-400ms validators) that would
    // otherwise leave a DOM footprint too brief for screen-readers and for
    // load-balanced Playwright polling to reliably observe. Per-instance via
    // <see cref="ValidatingStickyMs" />; set to 0 to opt out.
    /// <summary>
    ///     The default for <see cref="ValidatingStickyMs" />, in milliseconds.
    /// </summary>
    public const int DefaultValidatingStickyMs = 200;

    private readonly List<IAsyncFieldValidator> _asyncValidators = new();
    private readonly Dictionary<FieldIdentifier, DelegateRegistration> _fieldDelegates = new();
    private readonly Dictionary<FieldIdentifier, FieldState> _states = new();
    private readonly List<IFieldValidator> _validators = new();

    // The component that authored each field's bind expression (recorded when the control registers its
    // validator). A two-way write re-renders this consumer so derived UI it owns — even a sibling of the
    // Form, outside the control's own re-render scope — refreshes with no StateHasChanged. Mirrors the
    // controlled-mode AutoCallback owner-rerender, for bound mode.
    private readonly Dictionary<FieldIdentifier, Component?> _bindingOwners = new();
    private Delegate? _formDelegate;

    // What a validator that throws leaves on its field: a crashed rule must not crash the form.
    private const string CouldNotValidate = "Validation could not be completed.";

    /// <summary>Creates a context for <paramref name="model" />, the object whose fields are edited.</summary>
    /// <param name="model">The form's model. Fields bind to its properties, and to those of any nested
    ///     object reachable from it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model" /> is <see langword="null" />.</exception>
    public EditContext(object model) => Model = model ?? throw new ArgumentNullException(nameof(model));

    /// <summary>
    ///     Override the sticky window for this context. Default 200 ms.
    ///     Set to 0 to disable (the indicator disappears immediately when
    ///     PendingCount drops to 0 — the pre-sticky behaviour).
    /// </summary>
    public int ValidatingStickyMs { get; set; } = DefaultValidatingStickyMs;

    /// <summary>The object being edited — the model this context was created for.</summary>
    public object Model { get; }

    internal IEnumerable<FieldIdentifier> RegisteredFields => _states.Keys;

    // The key form-level messages are recorded under: the model itself, with no field name.
    private FieldIdentifier FormField => new(Model, string.Empty);

    /// <summary>
    ///     Whether anything registered here validates asynchronously — an <see cref="IAsyncFieldValidator" /> or
    ///     an async inline <c>Validate</c> delegate. <see cref="Validate" /> awaits them either way.
    /// </summary>
    public bool HasAsyncValidators => _asyncValidators.Count > 0 || HasAsyncDelegateValidators;

    /// <summary>
    ///     Whether any inline <c>Validate</c> delegate — on a field or on the form — is the asynchronous
    ///     kind. The narrower half of <see cref="HasAsyncValidators" />, which also counts registered
    ///     validator objects.
    /// </summary>
    public bool HasAsyncDelegateValidators
    {
        get
        {
            if (_formDelegate is not null && DelegateValidator.IsAsync(_formDelegate))
            {
                return true;
            }

            return _fieldDelegates.Values.Any(reg => DelegateValidator.IsAsync(reg.Validate));
        }
    }

    /// <summary>
    ///     Whether any field currently has a validator in flight. Use it to disable a submit button while
    ///     asynchronous checks finish.
    /// </summary>
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
    ///     UI re-renders to drop the Validation.Indicator when the sticky tail
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

    /// <summary>Raised when a field's value changes, with the field that changed.</summary>
    public event EventHandler<FieldChangedEventArgs>? FieldChanged;

    /// <summary>
    ///     Raised whenever the set of validation messages changes — one added, or some cleared. Not raised
    ///     when a re-validation produces exactly the messages that were already there.
    /// </summary>
    public event EventHandler? ValidationStateChanged;

    /// <summary>
    ///     Registers a synchronous validator for the whole form. Validators are de-duplicated by runtime
    ///     type, so registering the same kind twice — which a re-render does — adds nothing the second
    ///     time, and swapping in a different instance of a type already present has no effect.
    /// </summary>
    /// <param name="validator">The validator to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="validator" /> is <see langword="null" />.</exception>
    public void AddValidator(IFieldValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        // foreach rather than LINQ Any: the built-in passes are registered from a form's render path, so
        // this runs where a closure and an enumerator per call are worth not allocating.
        var t = validator.GetType();
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var existing in _validators)
#pragma warning restore S3267
        {
            if (existing.GetType() == t)
            {
                return;
            }
        }

        _validators.Add(validator);
    }

    /// <summary>
    ///     Registers an asynchronous validator for the whole form. De-duplicated by runtime type, exactly
    ///     as the synchronous overload is. <see cref="Validate" /> awaits it after the synchronous rules.
    /// </summary>
    /// <param name="validator">The validator to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="validator" /> is <see langword="null" />.</exception>
    public void AddValidator(IAsyncFieldValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var t = validator.GetType();
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var existing in _asyncValidators)
#pragma warning restore S3267
        {
            if (existing.GetType() == t)
            {
                return;
            }
        }

        // A store's rules stay last: they are asked once everything written in the app has passed.
        if (_asyncValidators is [.., StoreRuleValidator])
        {
            _asyncValidators.Insert(_asyncValidators.Count - 1, validator);
            return;
        }

        _asyncValidators.Add(validator);
    }

    // Lets a caller skip BUILDING a validator it would only have handed to AddValidator to discard.
    // The built-in passes are registered from Form.ResolveContext, which runs on every render — and a
    // form re-renders on every keystroke — so "allocate, then dedup" would be per-keystroke garbage in
    // a render hot path.
    internal bool HasValidator(Type validatorType)
    {
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var validator in _validators)
#pragma warning restore S3267
        {
            if (validator.GetType() == validatorType)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc cref="HasValidator" />
    internal bool HasAsyncValidator(Type validatorType)
    {
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var validator in _asyncValidators)
#pragma warning restore S3267
        {
            if (validator.GetType() == validatorType)
            {
                return true;
            }
        }

        return false;
    }

    // Per-field inline Validate delegate from Input/Select/Textarea factories. Passing
    // `validate: null` clears any prior registration so a re-render that drops the parameter
    // doesn't leave a stale callback in place. The value getter lets the dispatcher read the
    // current field value without reflecting on every validate call.
    /// <summary>
    ///     Registers the inline rule for one field, replacing any rule already registered for it. Passing
    ///     <see langword="null" /> removes it, which is how a re-render that no longer supplies a
    ///     <c>Validate</c> avoids leaving the old rule behind.
    /// </summary>
    /// <param name="field">The field the rule guards.</param>
    /// <param name="validate">The rule, or <see langword="null" /> to remove it.</param>
    /// <param name="valueGetter">Reads the field's current value, so the rule can run without reflecting
    ///     on every call.</param>
    public void RegisterFieldValidator(FieldIdentifier field, Delegate? validate, Func<object?> valueGetter)
    {
        if (validate is null)
        {
            _fieldDelegates.Remove(field);
            return;
        }

        _fieldDelegates[field] = new DelegateRegistration(validate, valueGetter);
    }

    /// <summary>
    ///     <see cref="RegisterFieldValidator(FieldIdentifier, Delegate?, Func{object?})" /> for callers with
    ///     no getter to hand, such as a test driving the context directly. Reads the value by reflection
    ///     instead, so under trimming the model's properties must be preserved.
    /// </summary>
    /// <param name="field">The field the rule guards.</param>
    /// <param name="validate">The rule, or <see langword="null" /> to remove it.</param>
    // Convenience overload for callers that don't have a getter handy (tests, direct API
    // use). Uses reflection over the model's runtime type to resolve the value.
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the model's runtime type — same constraint as DataAnnotations: " +
                        "the user-owned model's public properties are preserved by their binding setup.")]
    public void RegisterFieldValidator(FieldIdentifier field, Delegate? validate) =>
        RegisterFieldValidator(field, validate, () =>
            field.Model.GetType().GetProperty(field.FieldName)?.GetValue(field.Model));

    /// <summary>
    ///     Registers the form-level rule — the one for checks that span several fields, such as confirming
    ///     a password or ordering a date range. Replaces any previous one; <see langword="null" /> removes it.
    /// </summary>
    /// <param name="validate">The rule, or <see langword="null" /> to remove it.</param>
    // Form-level inline Validate delegate. Null clears.
    public void RegisterFormValidator(Delegate? validate) => _formDelegate = validate;

    // Latches the component currently mid-Render() as reading untracked EditContext state, so it
    // permanently opts out of the render cache (Component.RenderForLive) and re-executes Render() to
    // observe later validation-message / validating-state changes. Exactly the mechanism Context.Get
    // uses (Context.MarkConsumer). CurrentSync is non-null only during an active live render, so calls
    // from the validation/submit pipeline are no-ops — no over-marking, no allocation on the hot path.
    private static void MarkReader() => Live.LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();

    /// <summary>
    ///     Whether a validator is in flight for <paramref name="field" /> right now. This is the exact
    ///     answer, for control flow such as submit gating; <see cref="ShouldShowValidatingIndicator" /> is
    ///     the one to display.
    /// </summary>
    /// <param name="field">The field to ask about.</param>
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

    /// <summary>
    ///     Whether the user has changed this field's value since the form loaded.
    /// </summary>
    /// <param name="field">The field to ask about.</param>
    public bool IsModified(FieldIdentifier field)
    {
        MarkReader();
        return _states.TryGetValue(field, out var s) && s.Modified;
    }

    /// <summary>
    ///     Whether the user has visited and left this field. Showing errors only once a field is touched is
    ///     what stops a blank form shouting about every empty required field before anything is typed.
    /// </summary>
    /// <param name="field">The field to ask about.</param>
    public bool IsTouched(FieldIdentifier field)
    {
        MarkReader();
        return _states.TryGetValue(field, out var s) && s.Touched;
    }

    /// <summary>
    ///     The validation messages for one field, or an empty list when it has none.
    /// </summary>
    /// <param name="field">The field to ask about.</param>
    public IReadOnlyList<string> GetValidationMessages(FieldIdentifier field)
    {
        MarkReader();
        NoteMessagesRead(field);
        return _states.TryGetValue(field, out var s) ? s.Messages : Array.Empty<string>();
    }

    /// <summary>
    ///     Every validation message on the form, across all fields. Use
    ///     <see cref="GetValidationEntries" /> when you also need to know which field each belongs to.
    /// </summary>
    public IEnumerable<string> GetValidationMessages()
    {
        // Mark before returning the iterator: MarkReader() inside the yield body would only run on
        // first MoveNext (deferred), missing a render that enumerates lazily or not at all.
        MarkReader();
        NoteFormMessagesRead();
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

    /// <summary>
    ///     Every validation message paired with the name of the field it belongs to — what a validation
    ///     summary needs in order to link each message back to its input.
    /// </summary>
    public IReadOnlyList<ValidationEntry> GetValidationEntries()
    {
        MarkReader();
        NoteFormMessagesRead();
        var entries = new List<ValidationEntry>();
        foreach (var pair in _states)
            foreach (var m in pair.Value.Messages)
            {
                entries.Add(new ValidationEntry(pair.Key.FieldName, m));
            }

        return entries;
    }

    /// <summary>
    ///     Whether any field currently carries a validation message. Note this reports the messages
    ///     produced by the last run — it does not validate. Await <see cref="Validate" /> first to ask
    ///     whether the form is valid <em>now</em>.
    /// </summary>
    public bool HasValidationMessages()
    {
        MarkReader();
        return _states.Values.Any(s => s.Messages.Count > 0);
    }

    // Records the consumer that owns a field's bind expression so a write can re-render it. Idempotent per
    // render. A binding closed over a non-component root has no owner to re-render and is recorded all the
    // same: being here is what says a control on the form is bound to the field (IsBound).
    internal void TrackBindingOwner(FieldIdentifier field, Component? owner) => _bindingOwners[field] = owner;

    /// <summary>
    ///     Records that a field's value changed: marks it modified, raises <see cref="FieldChanged" />, and
    ///     re-renders the component that owns the binding so UI derived from the model refreshes too. The
    ///     built-in controls call this for you — you only need it when driving a control of your own.
    /// </summary>
    /// <param name="field">The field whose value changed.</param>
    public void NotifyFieldChanged(FieldIdentifier field)
    {
        var s = GetOrCreate(field);
        s.Modified = true;
        ClearFailuresNaming(field);
        FieldChanged?.Invoke(this, new FieldChangedEventArgs(field));

        // Re-render the binding's authoring component so its derived UI (including siblings outside the
        // Form / the control) reflects the new model value — the bound-mode counterpart of the
        // controlled-OnChange consumer re-render. The control already re-renders itself; this covers the host.
        if (_bindingOwners.TryGetValue(field, out var owner))
        {
            owner?.StateHasChanged();
        }
    }

    /// <summary>
    ///     Records that the user has visited and left a field — normally on blur. See
    ///     <see cref="IsTouched" /> for why that gates error display.
    /// </summary>
    /// <param name="field">The field the user left.</param>
    public void NotifyFieldTouched(FieldIdentifier field) =>
        GetOrCreate(field).Touched = true;

    /// <summary>
    ///     Removes the validation messages on one field, raising <see cref="ValidationStateChanged" /> only
    ///     if there were any to remove.
    /// </summary>
    /// <param name="field">The field to clear.</param>
    public void ClearMessages(FieldIdentifier field)
    {
        if (_states.TryGetValue(field, out var s) && s.Messages.Count > 0)
        {
            s.Messages.Clear();
            ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        }

        ClearFailuresNaming(field);
    }

    /// <summary>
    ///     Removes every validation message on the form. Each validation run starts with this, so call it
    ///     directly only to drop stale errors — after a reset, or when the model is replaced wholesale.
    /// </summary>
    public void ClearAllMessages()
    {
        var any = ClearFailures();
        foreach (var s in _states.Values.Where(s => s.Messages.Count > 0))
        {
            s.Messages.Clear();
            any = true;
        }

        if (any)
        {
            ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Idempotent on (field, message): a second identical add is a no-op — neither the
    // list nor the event observe it. Matches ASP.NET Core ModelStateDictionary's
    // duplicate-error suppression and closes the door on the "same message rendered twice"
    // class of bugs that can otherwise arise when a validator runs against the same field
    // through more than one path (full Validate + per-field re-validate, re-entrant Render
    // on a sync-context resume, etc.).
    /// <summary>
    ///     Attaches an error message to a field — the hook for errors only the server can produce, such as
    ///     "that email is already registered" coming back from a failed submit.
    ///     <para>
    ///         Adding the same message to the same field twice is a no-op, so a field validated through
    ///         more than one path does not show its error twice.
    ///     </para>
    /// </summary>
    /// <param name="field">The field the message belongs to.</param>
    /// <param name="message">The message, written for the person reading it.</param>
    public void AddValidationMessage(FieldIdentifier field, string message)
    {
        var state = GetOrCreate(field);
        if (state.Messages.Contains(message, StringComparer.Ordinal))
        {
            return;
        }

        state.Messages.Add(message);
        ValidationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Marks every field the form knows about as touched, so validation messages that were held back
    ///     until a field was visited all become visible. This is what a rejected submit does — the user
    ///     asked to proceed, so every reason they cannot should now be on screen at once.
    /// </summary>
    public void TouchAllRegisteredFields()
    {
        foreach (var s in _states.Values)
        {
            s.Touched = true;
        }

        // Field delegates may target fields that haven't been touched by a binding yet, so
        // make sure their messages survive the next per-keystroke gate.
        foreach (var field in _fieldDelegates.Keys)
        {
            GetOrCreate(field).Touched = true;
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
