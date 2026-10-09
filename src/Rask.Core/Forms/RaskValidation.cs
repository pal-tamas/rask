namespace Rask.Core.Forms;

// The built-in validation switchboard.
//
// Two jobs, and the second one is why this type exists at all:
//
//   1. AutoValidate — the global default a Form reads before registering anything.
//   2. Validator sources — the inversion that lets Core use FluentValidation without referencing it.
//      Rask.Core cannot take a dependency on the FluentValidation package (Core is bundled into every
//      host and must stay third-party-free), so the dependency runs the other way: the
//      Rask.Validation.FluentValidation assembly announces itself with a [ModuleInitializer] that calls
//      RegisterSource, exactly the handshake src/Rask/Browser/WasmHostBuilderExtensions.cs uses for the
//      WASM batteries. Nothing here knows what FluentValidation is.
//
// A source answers TWO questions, and the split is load-bearing. "Is there a validator for this type?"
// is asked on every render and must not build anything: constructing eagerly meant a validator with
// constructor dependencies threw out of Render() when there was no scope to resolve them from, and it
// froze the rules at first render so editing a RuleFor and hot-reloading changed nothing.
/// <summary>
///     Controls Rask's built-in validation.
///     <para>
///         Validation is on by default: a <c>Form</c> validates its model's
///         <c>System.ComponentModel.DataAnnotations</c> attributes, and any validator discovered for the
///         model type, with nothing declared. Set <see cref="AutoValidate" /> to <see langword="false" />
///         to turn that off everywhere, or take a form out of it individually with the form's own
///         <c>AutoValidate</c> step.
///     </para>
/// </summary>
public static class RaskValidation
{
    private static readonly Lock Gate = new();
    private static volatile ValidatorSource[] _sources = [];
    private static volatile Dictionary<Type, Func<IServiceProvider?, IStoreRules?>> _storeRules = [];
    private static volatile Dictionary<Type, Func<string, Delegate?>> _fieldRules = [];

    /// <summary>
    ///     Whether a <c>Form</c> validates its model with no validator declared. <see langword="true" />
    ///     by default.
    ///     <para>
    ///         An app hosted by the <c>Rask</c> package says this as
    ///         <c>app.Configure(c =&gt; c.Validation.Off())</c>, which sets this property; setting it
    ///         directly is how a lean host or a WebAssembly app does the same thing.
    ///     </para>
    /// </summary>
    public static bool AutoValidate { get; set; } = true;

    /// <summary>
    ///     Registers a source of validators for model types — the seam an integration package plugs into
    ///     so Core can use it without referencing it.
    ///     <para>
    ///         Sources are consulted in registration order and the first answer wins. Registering the
    ///         same source twice registers it twice; call this once, from a <c>[ModuleInitializer]</c>.
    ///     </para>
    /// </summary>
    /// <param name="has">
    ///     Whether this source has a validator for the type. Asked on every render, so it must not build
    ///     one.
    /// </param>
    /// <param name="resolve">
    ///     Builds the validator, given the scope its own constructor dependencies come from. Asked once
    ///     per validation run, not per render.
    /// </param>
    public static void RegisterSource(
        Func<Type, bool> has,
        Func<Type, IServiceProvider?, IAsyncFieldValidator?> resolve)
    {
        ArgumentNullException.ThrowIfNull(has);
        ArgumentNullException.ThrowIfNull(resolve);

        // Copy-on-write: the readers below take no lock, so they must never see a half-built array.
        lock (Gate)
        {
            var next = new ValidatorSource[_sources.Length + 1];
            Array.Copy(_sources, next, _sources.Length);
            next[^1] = new ValidatorSource(has, resolve);
            _sources = next;
        }
    }

    /// <summary>
    ///     Whether anything has a validator for <paramref name="modelType" />. Answers without building
    ///     one, so a form can ask on every render.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <returns><see langword="true" /> when some source can supply a validator.</returns>
    public static bool HasValidatorFor(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var source in _sources)
#pragma warning restore S3267
        {
            if (source.Has(modelType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Builds the validator for <paramref name="modelType" />, or <see langword="null" /> when
    ///     nothing validates it.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <param name="services">The scope a validator's constructor dependencies come from.</param>
    /// <returns>A validator for the model, or <see langword="null" />.</returns>
    public static IAsyncFieldValidator? Resolve(Type modelType, IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        foreach (var source in _sources)
        {
            if (source.Create(modelType, services) is { } validator)
            {
                return validator;
            }
        }

        return null;
    }

    /// <summary>
    ///     Registers the rules the properties of one model type carry in their own types — a value object's
    ///     <c>Validate</c> — so a field bound to such a property runs its rule with no <c>Validate</c> step
    ///     written on it. The seam a data layer's generator plugs into; call it once per model type, from a
    ///     <c>[ModuleInitializer]</c>.
    ///     <para>
    ///         The rule runs first, exactly as a <c>Validate</c> step written first on the field would: on the
    ///         field's own bind timing and on submit, and the steps the field does write run after it, only
    ///         once it has let the value through. <see cref="AutoValidate" /> does not switch it off.
    ///         Registering a type again replaces what it had.
    ///     </para>
    /// </summary>
    /// <param name="modelType">
    ///     The type that declares the properties, exactly: the form's model, or the type of an object or a
    ///     row nested in it. A type derived from it is not covered.
    /// </param>
    /// <param name="rule">
    ///     Given a property's name, hands back its rule or <see langword="null" /> when it has none: a
    ///     <see cref="Validate{T}" /> or a <c>Func&lt;T, ValueTask&lt;IEnumerable&lt;string&gt;&gt;&gt;</c>
    ///     over the property's declared type <c>T</c>, the same two shapes a <c>Validate</c> step takes.
    ///     Asked on every render of a bound field, so it hands back a delegate it keeps, and builds none.
    /// </param>
    public static void RegisterFieldRules(Type modelType, Func<string, Delegate?> rule)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(rule);

        lock (Gate)
        {
            _fieldRules = new Dictionary<Type, Func<string, Delegate?>>(_fieldRules) { [modelType] = rule };
        }
    }

    // What a bound field validates with: the rule its property's type carries, then what the field wrote.
    // One lookup and nothing built for a model no one registered, which is every render of most forms.
    internal static Delegate? RuleFor<T>(FieldIdentifier field, Delegate? written)
    {
        var rules = _fieldRules;
        if (rules.Count == 0 || !rules.TryGetValue(field.Model.GetType(), out var ruleOf)
            || ruleOf(field.FieldName) is not { } own)
        {
            return written;
        }

        if (own is not (Validate<T> or Func<T, ValueTask<IEnumerable<string>>>))
        {
            throw new InvalidOperationException(
                $"The rule registered for {field} is a {own.GetType().Name}, and the field is bound as "
                + $"{typeof(T).Name}: RaskValidation.RegisterFieldRules must hand back a Validate<{typeof(T).Name}> "
                + $"or a Func<{typeof(T).Name}, ValueTask<IEnumerable<string>>> for it.");
        }

        return written is null ? own : RuleSequence<T>.Of(own, written);
    }

    /// <summary>
    ///     Registers the rules a store keeps for one form model — a unique index, a range that must not
    ///     overlap — so a form over that model checks them as each field is committed and again on submit,
    ///     with nothing written on the form. The seam a data layer plugs into; call it once per model, from
    ///     a <c>[ModuleInitializer]</c>.
    ///     <para>
    ///         These are rules somebody wrote, on the index, so <see cref="AutoValidate" /> does not switch
    ///         them off. Registering a model again replaces what it had.
    ///     </para>
    /// </summary>
    /// <param name="modelType">
    ///     The form model's type, exactly: a form over a type derived from it is not checked.
    /// </param>
    /// <param name="resolve">
    ///     Hands back the rules, given the scope the store is reached through, or <see langword="null" />
    ///     where there is no store to ask. Asked once per check, never during a render.
    /// </param>
    public static void RegisterStoreRules(Type modelType, Func<IServiceProvider?, IStoreRules?> resolve)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(resolve);

        // Copy-on-write, as the sources above: a form asks on every render and takes no lock.
        lock (Gate)
        {
            _storeRules = new Dictionary<Type, Func<IServiceProvider?, IStoreRules?>>(_storeRules) { [modelType] = resolve };
        }
    }

    /// <summary>
    ///     Whether a store has registered rules for <paramref name="modelType" />. Answers without reaching
    ///     the store, so a form can ask on every render.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <returns><see langword="true" /> when the model's fields are checked against a store.</returns>
    public static bool HasStoreRulesFor(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        var rules = _storeRules;
        return rules.Count > 0 && rules.ContainsKey(modelType);
    }

    internal static IStoreRules? ResolveStoreRules(Type modelType, IServiceProvider? services) =>
        _storeRules.TryGetValue(modelType, out var resolve) ? resolve(services) : null;

    private readonly record struct ValidatorSource(
        Func<Type, bool> Has,
        Func<Type, IServiceProvider?, IAsyncFieldValidator?> Create);
}
