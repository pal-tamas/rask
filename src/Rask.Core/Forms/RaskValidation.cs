using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

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
    private static volatile RequiredFields _requiredFields = RequiredFields.None;
    private static volatile FieldShapes _fieldShapes = FieldShapes.None;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<string, string?>> Clearing = new();
    private static volatile ConcurrentDictionary<(Type Owner, string Name), FieldShape?> _statedShapes = new();

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
    ///     over the property's declared type <c>T</c>, the same two shapes a <c>Validate</c> step takes — or a
    ///     <c>Func&lt;object, T, IEnumerable&lt;string&gt;&gt;</c>, handed the object that holds the property first, for
    ///     a rule that reads the fields beside it (a range's end against its start). A
    ///     control over a value type bound to a <c>bool?</c> or an <c>int?</c> — a checkbox, a switch — is
    ///     handed the rule over the nullable type, and asks it about what the property holds, null included.
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
    // Under an object that may be left wholly empty the type's rule is asked only about a value that was
    // filled in: a field's rule sees its own value, not the fields beside it, so it cannot tell an object nobody
    // gave from one that lacks this field — the write can, and says so under the field.
    internal static Delegate? RuleFor<T>(FieldIdentifier field, Delegate? written, bool underOptional = false)
    {
        var rules = _fieldRules;
        if (rules.Count == 0 || !rules.TryGetValue(field.Model.GetType(), out var ruleOf)
            || ruleOf(field.FieldName) is not { } own)
        {
            return written;
        }

        // A rule that reads the fields beside its own is asked with the object that holds them. One over another type
        // than the control's — a slider over double bound to a double? end — is left to the write, which runs it.
        if (BesideRule.Reads(own))
        {
            if (BesideRule.Over<T>(own, field.Model) is not { } beside)
            {
                return written;
            }

            own = beside;
        }

        if (own is Validate<T> or Func<T, ValueTask<IEnumerable<string>>>)
        {
            if (underOptional)
            {
                own = WhenFilled.Of<T>(own);
            }

            return written is null ? own : RuleSequence<T>.Of(own, written);
        }

        // A control over a value type bound to the property's nullable twin: the rule is over what the property holds.
        if (LiftedRule.IsOverNullable<T>(own))
        {
            return LiftedRule.Then(own, written, underOptional);
        }

        throw new InvalidOperationException(
            $"The rule registered for {field} is a {own.GetType().Name}, and the field is bound as "
            + $"{typeof(T).Name}: RaskValidation.RegisterFieldRules must hand back a Validate<{typeof(T).Name}> "
            + $"or a Func<{typeof(T).Name}, ValueTask<IEnumerable<string>>> for it.");
    }

    /// <summary>
    ///     Registers which properties of one model type must be filled because a rule the type carries says
    ///     so — a property that is required by its own type, with no <c>[Required]</c> written on it — so a
    ///     control bound to one announces it. The seam a data layer's generator plugs into; call it once per
    ///     model type, from a <c>[ModuleInitializer]</c>.
    ///     <para>
    ///         It says a field is required and enforces nothing: register only a property a rule refuses when
    ///         it is left empty, the one <see cref="RegisterFieldRules" /> hands a rule back for. Registering a
    ///         type again replaces what it had.
    ///     </para>
    /// </summary>
    /// <param name="modelType">
    ///     The type that declares the properties: the form's model, or the type of an object or a row nested
    ///     in it. A type derived from it is covered too.
    /// </param>
    /// <param name="isRequired">
    ///     Given a property's name, whether it must be filled. Asked once per property and the answer kept
    ///     until a type is registered again, so it answers the same every time and may be asked from any
    ///     thread.
    /// </param>
    public static void RegisterRequiredFields(Type modelType, Func<string, bool> isRequired)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(isRequired);

        // A whole new set, answers and all: a bound control asks on every render and takes no lock.
        lock (Gate)
        {
            _requiredFields = _requiredFields.With(modelType, isRequired);
        }
    }

    /// <summary>
    ///     Registers which properties of one model type hold an object that may be left wholly empty — an
    ///     optional part of the model, which is not given at all when none of its fields is filled — so a
    ///     control bound to a field under one does not announce it as required, and the rule its type carries
    ///     is asked only about a value that was filled in. The seam a data layer's generator plugs into; call
    ///     it once per model type, from a <c>[ModuleInitializer]</c>.
    ///     <para>
    ///         It is said of the property that holds the object, not of the object's type, because one type
    ///         can be held both ways: <c>Shipping</c> must be there and <c>Billing</c> may be absent, and both
    ///         are an address. It relaxes what a form asks and enforces nothing: whoever saves the model still
    ///         says which fields a part that <i>was</i> given lacks. Registering a type again replaces what
    ///         it had.
    ///     </para>
    /// </summary>
    /// <param name="modelType">
    ///     The type that declares the properties: the form's model, or the type of an object or a row nested
    ///     in it. A type derived from it is covered too.
    /// </param>
    /// <param name="isOptional">
    ///     Given a property's name, whether the object it holds may be left wholly empty. Asked on every
    ///     render of a control bound under the type, so it answers the same every time, builds nothing and
    ///     may be asked from any thread.
    /// </param>
    public static void RegisterOptionalFields(Type modelType, Func<string, bool> isOptional)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(isOptional);

        lock (Gate)
        {
            _requiredFields = _requiredFields.WithOptional(modelType, isOptional);
        }
    }

    /// <summary>
    ///     Whether a field must be filled: its type registered it through
    ///     <see cref="RegisterRequiredFields" />, or the member carries
    ///     <see cref="System.ComponentModel.DataAnnotations.RequiredAttribute" />. The one question a form
    ///     control asks to announce a required field, as <c>aria-required</c>; cheap enough to ask on every
    ///     render.
    /// </summary>
    /// <param name="modelType">
    ///     The type the path starts from. A bound control passes the type of the object that owns its
    ///     property, with the property's name.
    /// </param>
    /// <param name="fieldPath">
    ///     A property's name, or the path a field failure names it by: <c>Price.Amount</c>,
    ///     <c>Rows[2].Weight</c>. A path is followed through the declared types of the properties it walks —
    ///     an index stands for the collection's item type — and the question is put to the type that owns the
    ///     last property, and to its base types: <c>Rows[2].Weight</c> asks what the row's type registered
    ///     for <c>Weight</c>.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when the field must be filled; <see langword="false" /> when nothing says
    ///     so, or the path names no member.
    /// </returns>
    internal static bool IsRequired(Type modelType, string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(fieldPath);
        return _requiredFields.IsRequired(modelType, fieldPath);
    }

    /// <summary>
    ///     Whether the field a bind expression ends in must be filled: its last property carries
    ///     <c>[Required]</c> or was registered (<see cref="RegisterRequiredFields" />), unless the expression walks
    ///     through an object that may be left wholly empty (<see cref="RegisterOptionalFields" />). The
    ///     question a control that draws itself asks with the expression it was bound with; nothing is
    ///     evaluated and nothing is built, so it is cheap enough to ask on every render.
    /// </summary>
    /// <param name="bound">The control's bind expression: <c>() =&gt; model.Billing.Street</c>.</param>
    /// <returns>
    ///     <see langword="true" /> when the field must be filled; <see langword="false" /> when nothing says
    ///     so, when it lies under an object that may be absent, or when the expression ends in no member.
    /// </returns>
    public static bool IsRequired(LambdaExpression bound)
    {
        ArgumentNullException.ThrowIfNull(bound);

        var body = bound.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } lifted)
        {
            body = lifted.Operand;
        }

        var fields = _requiredFields;
        return body is MemberExpression { Expression: { } holder } member
               && fields.IsRequired(holder.Type, member.Member.Name)
               && !fields.IsUnderOptional(holder);
    }

    // The same for a control Core draws, which has parsed its bind expression already.
    internal static bool IsRequired(ExpressionAccessor.Accessor bound) =>
        !bound.UnderOptional && _requiredFields.IsRequired(bound.Target.GetType(), bound.PropertyName);

    internal static bool IsUnderOptional(Expression? holder) => _requiredFields.IsUnderOptional(holder);

    /// <summary>
    ///     Registers what each field of one model type is — its <see cref="FieldShape" />: the kind of value it holds
    ///     and the rules its value object declares (<c>Text(Value, MaxLength: 1000)</c>) — so a control bound to one is
    ///     told them up front: a text input its <c>maxlength</c> and <c>minlength</c>, instead of found out on submit.
    ///     The seam a data layer's generator plugs into; call it once per model type, from a <c>[ModuleInitializer]</c>.
    ///     Registering a type again replaces what it had.
    /// </summary>
    /// <param name="modelType">The type that declares the properties. A type derived from it is covered too.</param>
    /// <param name="shapeOf">
    ///     Given a property's name, its shape, or <see langword="null" /> for a property that has none to tell — one whose
    ///     own type says all there is. Asked once per property and the answer kept until a type is registered again. A
    ///     shape given is the whole answer, the hints of the property's attributes included (a generated model writes
    ///     them in); a property answered null is read off its DataAnnotations attributes instead, as a model that
    ///     registered nothing is.
    /// </param>
    public static void RegisterFieldShapes(Type modelType, Func<string, FieldShape?> shapeOf)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(shapeOf);

        lock (Gate)
        {
            _fieldShapes = _fieldShapes.With(modelType, shapeOf);
        }
    }

    /// <summary>
    ///     Registers, for one form model, the fields whose value may be cleared rather than kept — an operation's
    ///     <c>Change&lt;Nickname?&gt;</c> — each with the <see langword="bool" /> property that says it is to be:
    ///     <c>Ui.Field.Bind(() =&gt; f.Nick)</c> then draws a "Clear" beside its control, bound to that property. Called by
    ///     generated code, from a <c>[ModuleInitializer]</c>.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <param name="clearedBy">The property that clears a field, by the field's name; null for one that is not cleared.</param>
    public static void RegisterClearing(Type modelType, Func<string, string?> clearedBy)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(clearedBy);
        Clearing[modelType] = clearedBy;
    }

    // The bool property that clears the field a control is bound to, or null for one that is kept or set only.
    internal static string? ClearedByOf(ExpressionAccessor.Accessor bound) =>
        Clearing.TryGetValue(bound.Target.GetType(), out var clearedBy) ? clearedBy(bound.Property.Name) : null;

    // The shape of the field a control is bound to: what its model registered, else what the member's DataAnnotations
    // attributes say (a hand-written model), else null.
    internal static FieldShape? ShapeOf(ExpressionAccessor.Accessor bound) => ShapeOf(bound.Target.GetType(), bound.Property);

    // The same for a member of a type with no instance in hand: a list's column, named by `r => r.Plate`. What rules
    // stated in code (StatedRules) add is merged in last, once per property and kept until they change.
    internal static FieldShape? ShapeOf(Type owner, PropertyInfo property)
    {
        var shape = _fieldShapes.ShapeOf(owner, property.Name) ?? AnnotatedShapes.Of(property);
        return StatedRules.Source is null
            ? shape
            : _statedShapes.GetOrAdd(
                (owner, property.Name),
                static (field, given) => StatedRules.Shape(field.Owner, field.Name, given.Property.PropertyType, given.Shape),
                (Property: property, Shape: shape));
    }

    // Everything worked out from registrations and stated rules, asked again: a hot reload changed what is stated.
    internal static void ForgetAnswers()
    {
        lock (Gate)
        {
            _fieldShapes = _fieldShapes.Fresh();
            _requiredFields = _requiredFields.Fresh();
            _statedShapes = new();
        }
    }

    // The most characters the field a control is bound to may hold, or null: what its maxlength says.
    internal static int? MaxLengthOf(ExpressionAccessor.Accessor bound) => ShapeOf(bound)?.MaxLength;

    // The fewest characters the field a control is bound to may hold, or null: what its minlength says.
    internal static int? MinLengthOf(ExpressionAccessor.Accessor bound) => ShapeOf(bound)?.MinLength;

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
