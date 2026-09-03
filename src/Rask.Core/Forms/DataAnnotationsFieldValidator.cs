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
///         A <c>Form</c> registers this for you — see <see cref="RaskValidation.AutoValidate" /> to turn
///         that off. Client-side validation is a convenience, never a control: always validate again on
///         the server.
///     </para>
/// </summary>
public sealed class DataAnnotationsFieldValidator : IFieldValidator
{
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

        foreach (var node in ModelGraphWalker.Walk(context.Model))
        {
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
        Validator.TryValidateProperty(prop.GetValue(owner), ctx, results);

        foreach (var r in results)
        {
            context.AddValidationMessage(field, r.ErrorMessage ?? "Invalid value.");
        }

        // IValidatableObject is a whole-object rule, so re-run it for per-field revalidation
        // and surface only results that name this field. Cross-field rules referencing the
        // active field stay reactive; rules targeting other fields don't bleed in.
        if (owner is IValidatableObject validatable)
        {
            var fullCtx = NewValidationContext(owner);
            foreach (var r in validatable.Validate(fullCtx))
            {
                if (r.MemberNames.Contains(field.FieldName))
                {
                    context.AddValidationMessage(field, r.ErrorMessage ?? "Invalid value.");
                }
            }
        }
    }

    // One node's failures, flattened to (memberName, message). An empty member name is the
    // object-level slot — FieldIdentifier(node, "") for a form, the "" key on the wire.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TryValidateObject reflects over the node's runtime type — same trim contract " +
                        "as the root: the consumer is responsible for preserving every reachable model " +
                        "type's public properties, typically via [DynamicallyAccessedMembers].")]
    private IEnumerable<(string Member, string Message)> ResultsFor(object node)
    {
        var results = new List<ValidationResult>();
        var ctx = NewValidationContext(node);
        Validator.TryValidateObject(node, ctx, results, true);

        // BCL's TryValidateObject short-circuits IValidatableObject.Validate as soon as any
        // attribute-level error is found. ASP.NET Core MVC's DefaultObjectValidator does not
        // — attribute and IValidatableObject errors accumulate together. Invoke the interface
        // method ourselves to match that experience.
        if (node is IValidatableObject validatable)
        {
            foreach (var r in validatable.Validate(ctx))
            {
                results.Add(r);
            }
        }

        foreach (var r in results)
        {
            var message = r.ErrorMessage ?? "Invalid value.";
            var members = r.MemberNames.ToList();
            if (members.Count == 0)
            {
                yield return (string.Empty, message);
                continue;
            }

            foreach (var m in members)
            {
                yield return (m, message);
            }
        }
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
