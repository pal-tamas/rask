using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

// The DataAnnotations pass, built in. This used to be an opt-in component the user placed inside the
// Form (Rask.Validation.DataAnnotations.DataAnnotationsValidator); it now lives in Core and Form
// registers it itself, so [Required] on a model just works. The behaviour is carried over verbatim —
// the only thing that changed is who constructs it.
//
// EditContext.AddValidator dedups by runtime type, which is what makes registering on every render
// free: the second and later registrations are discarded.
/// <summary>
///     Validates a model with its <c>System.ComponentModel.DataAnnotations</c> attributes
///     (<c>[Required]</c>, <c>[EmailAddress]</c>, <c>[StringLength]</c>, …), including
///     <see cref="IValidatableObject" /> and every nested sub-object and collection item reachable
///     from the root.
///     <para>
///         The asynchronous rules of .NET 11 — an <see cref="AsyncValidationAttribute" /> and an
///         <see cref="IAsyncValidatableObject" /> — are awaited by the asynchronous half a <c>Form</c> registers beside
///         this one; for those objects this pass asks only the synchronous attributes.
///     </para>
///     <para>
///         A <c>Form</c> registers this for you — see <see cref="RaskValidation.AutoValidate" /> to turn
///         that off. Client-side validation is a convenience, never a control: always validate again on
///         the server.
///     </para>
/// </summary>
public sealed class DataAnnotationsFieldValidator : IFieldValidator
{
    // What a failure that brought no message of its own says.
    internal const string Invalid = "Invalid value.";

    private readonly IServiceProvider? _services;

    /// <summary>
    ///     Creates the validator.
    /// </summary>
    /// <param name="services">
    ///     The scope used to build each <see cref="ValidationContext" />, so a custom
    ///     <see cref="ValidationAttribute" /> can call <c>validationContext.GetService&lt;T&gt;()</c>.
    ///     May be <see langword="null" /> outside a live context, matching MVC's behaviour when no
    ///     service provider is configured.
    /// </param>
    public DataAnnotationsFieldValidator(IServiceProvider? services = null) => _services = services;

    /// <summary>
    ///     Validates <paramref name="model" /> and everything reachable from it, returning one entry per
    ///     failed rule. This is the same pass <see cref="Validate(EditContext)" /> runs, shaped for callers that have
    ///     no <see cref="EditContext" /> — a dispatched CQRS request, for instance.
    /// </summary>
    /// <param name="model">The object to validate.</param>
    /// <param name="services">The scope custom attributes resolve services from.</param>
    /// <returns>Every validation failure; empty when the model is valid.</returns>
    public static IReadOnlyList<ValidationEntry> Validate(object model, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var entries = new List<ValidationEntry>();
        var validator = new DataAnnotationsFieldValidator(services);

        foreach (var node in ModelGraphWalker.Walk(model))
        {
            foreach (var (member, message) in validator.ResultsFor(node))
            {
                entries.Add(new ValidationEntry(member, message));
            }
        }

        return entries;
    }

    // The same pass for a caller that can await: a dispatched request, an endpoint. An object with async rules
    // (AsyncAnnotationRules) is validated as .NET's asynchronous Validator would — its async attributes awaited, never
    // asked synchronously — and every other object exactly as Validate above validates it.
    internal static async ValueTask<IReadOnlyList<ValidationEntry>> ValidateAwaited(
        object model, IServiceProvider? services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var entries = new List<ValidationEntry>();
        var validator = new DataAnnotationsFieldValidator(services);
        foreach (var node in ModelGraphWalker.Walk(model).ToList())
        {
            var results = AsyncAnnotationRules.Of(node.GetType()) is { } rules
                ? await AsyncAnnotationPass.Results(node, rules, services, faulted: null, cancellationToken).ConfigureAwait(false)
                : validator.ResultsFor(node);
            foreach (var (member, message) in results)
            {
                entries.Add(new ValidationEntry(member, message));
            }
        }

        return entries;
    }

    /// <summary>
    ///     What a failed DataAnnotations attribute says, in the reader's language: <paramref name="said" /> — .NET's
    ///     English, or the attribute's own words — unless <paramref name="check" /> is a built-in attribute with no
    ///     <c>ErrorMessage</c> of its own and the <c>RaskStrings</c> catalog has its sentence in the reader's language.
    /// </summary>
    /// <remarks>
    ///     What this validator does with every failure it reports. For code that runs an attribute itself —
    ///     <c>check.GetValidationResult(value, context)</c>, as a Rask.Data model's generated rules do — and shows
    ///     what it said: <c>[StringLength(100)]</c> says "Maximum 100 karakter hosszúnak kell lennie." to a Hungarian
    ///     reader, and .NET's own text to an English one.
    /// </remarks>
    /// <param name="check">The attribute that failed.</param>
    /// <param name="value">The value it failed on.</param>
    /// <param name="said">What the attribute's <see cref="ValidationResult" /> said.</param>
    /// <returns>The sentence to show.</returns>
    public static string Message(ValidationAttribute check, object? value, string said)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(said);
        return AttributeMessages.For(check, value) ?? said;
    }

    // ValidationContext/Validator reflect over the model graph. The user-owned model types are
    // preserved through binding annotations on their app (Form<TModel>'s type parameter is
    // DAM-annotated), not by Rask.Core itself.
    /// <summary>
    ///     Validates the whole model graph, attaching a message to each failing field.
    /// </summary>
    /// <param name="context">The context whose <see cref="EditContext.Model" /> is validated.</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ValidationContext/Validator reflect over the model graph. The user-owned model " +
                        "types are preserved through Form<TModel>'s DynamicallyAccessedMembers annotation, " +
                        "not by Rask.Core itself.")]
    public void Validate(EditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // An object with async rules is the asynchronous half's, whole, when the form has it: asked here, an async
        // attribute would be asked synchronously, and an IAsyncValidatableObject's synchronous Validate run as well.
        var awaited = context.HasAsyncValidator(typeof(DataAnnotationsAsyncValidator));
        foreach (var node in ModelGraphWalker.Walk(context.Model))
        {
            if (awaited && AsyncAnnotationRules.Of(node.GetType()) is not null)
            {
                continue;
            }

            foreach (var (member, message) in ResultsFor(node))
            {
                context.AddValidationMessage(new FieldIdentifier(node, member), message);
            }
        }
    }

    /// <summary>
    ///     Validates a single field against the attributes on its immediate owner.
    /// </summary>
    /// <param name="context">The context to attach messages to.</param>
    /// <param name="field">The field being validated.</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Same as Validate(): user-owned model preservation is the caller's responsibility.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the field-owner's runtime type — preserved by the user's binding setup.")]
    public void ValidateField(EditContext context, FieldIdentifier field)
    {
        ArgumentNullException.ThrowIfNull(context);

        // field.Model is always the immediate owner of field.FieldName (reference-based
        // FieldIdentifier scheme), so per-field validation works at any depth without
        // walking the graph — TryValidateProperty just runs against the owner directly.
        var owner = field.Model;
        var prop = owner.GetType().GetProperty(field.FieldName);
        if (prop is null)
        {
            return;
        }

        var ctx = NewValidationContext(owner);
        ctx.MemberName = field.FieldName;
        var results = new List<ValidationResult>();

        // With the asynchronous half on the form, a field's async attributes and an IAsyncValidatableObject's rules
        // are its to await: this asks the field's synchronous attributes only, and those first.
        var rules = context.HasAsyncValidator(typeof(DataAnnotationsAsyncValidator))
            ? AsyncAnnotationRules.Of(owner.GetType())
            : null;
        if (rules?.Find(field.FieldName) is { Async.Length: > 0 } member)
        {
            Validator.TryValidateValue(prop.GetValue(owner), ctx, results, member.AllSync);
        }
        else
        {
            Validator.TryValidateProperty(prop.GetValue(owner), ctx, results);
        }

        foreach (var r in results)
        {
            context.AddValidationMessage(field, Worded(r, owner, field.FieldName));
        }

        // IValidatableObject is a whole-object rule, so re-run it for per-field revalidation
        // and surface only results that name this field. Cross-field rules referencing the
        // active field stay reactive; rules targeting other fields don't bleed in.
        if (owner is IValidatableObject validatable && rules is not { AsyncObject: true })
        {
            var fullCtx = NewValidationContext(owner);
            foreach (var r in validatable.Validate(fullCtx)
                         .Where(r => r.MemberNames.Contains(field.FieldName, StringComparer.Ordinal)))
            {
                context.AddValidationMessage(field, r.ErrorMessage ?? Invalid);
            }
        }
    }

    // A failure's message, in the reader's language where it is a built-in attribute's own English (AttributeMessages).
    private string Worded(ValidationResult result, object owner, string member) =>
        result.ErrorMessage is { } said ? AttributeMessages.Reworded(said, owner, member, _services) : Invalid;

    // One node's failures, flattened to (memberName, message). An empty member name is the
    // object-level slot — FieldIdentifier(node, "") for a form, the "" key on the wire.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TryValidateObject reflects over the node's runtime type — same trim contract " +
                        "as the root: the consumer is responsible for preserving every reachable model " +
                        "type's public properties, typically via [DynamicallyAccessedMembers].")]
    private List<(string Member, string Message)> ResultsFor(object node)
    {
        var results = new List<ValidationResult>();
        var ctx = NewValidationContext(node);
        Validator.TryValidateObject(node, ctx, results, true);

        // BCL's TryValidateObject short-circuits IValidatableObject.Validate as soon as any
        // attribute-level error is found. ASP.NET Core MVC's DefaultObjectValidator does not
        // — attribute and IValidatableObject errors accumulate together. Invoke the interface
        // method ourselves to match that experience.
        //
        // Which means it can run TWICE: with no attribute errors, TryValidateObject already ran it and
        // its results are in the list. The count cannot tell the two cases apart — the BCL puts
        // attribute errors and IValidatableObject errors in the same list — so the duplicate is removed
        // below instead of predicted here.
        if (node is IValidatableObject validatable)
        {
            foreach (var r in validatable.Validate(ctx))
            {
                results.Add(r);
            }
        }

        // Dedup by (field, message), which is what the form path always did: EditContext.AddValidationMessage
        // drops a message already on the field, so the double run above was invisible there. The static
        // pass has no EditContext behind it, and without this a dispatched request would put every
        // object-level failure on the wire twice.
        var seen = new HashSet<(string, string)>();
        var failures = new List<(string Member, string Message)>();

        // ValidationResult.Success IS null, and an IValidatableObject is free to yield it — the BCL
        // filters those out, so anything hand-rolling this loop has to as well or it dereferences
        // null on a model that only said "this one is fine". OfType is that filter.
        foreach (var r in results.OfType<ValidationResult>())
        {
            var members = r.MemberNames.Where(static m => m is not null).ToList();
            var message = members.Count == 1 ? Worded(r, node, members[0]) : r.ErrorMessage ?? Invalid;
            IEnumerable<string> fields = members.Count == 0 ? [string.Empty] : members;

            // HashSet.Add is the "not seen yet" test: it records the pair as it passes it.
            failures.AddRange(fields.Select(m => (m, message)).Where(seen.Add));
        }

        return failures;
    }

    // ASP.NET Core / Blazor parity: ValidationContext is constructed with the render-scoped
    // IServiceProvider so custom ValidationAttribute subclasses can call
    // validationContext.GetService<T>() at validation time. The provider is captured once when the
    // validator is constructed — action invocation (submit, change, blur) doesn't re-enter
    // LiveRenderContext, so reading it at validation time would return null.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ValidationContext's no-display-name ctor reflects for DisplayNameAttribute on the " +
                        "model — same constraint as Validate/ValidateField: the user-owned model type is " +
                        "preserved through the consumer's binding/page annotations.")]
    private ValidationContext NewValidationContext(object model) =>
        new(model, _services, null);
}
