using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Validation.DataAnnotations;

// Opt-in DataAnnotations validator. Place inside a Form as a child:
//
//   Form<RegistrationModel>(_model, OnValidSubmit: ...)[
//       DataAnnotationsValidator(),
//       Input(() => _model.Email),
//       ValidationMessage(() => _model.Email, ...)
//   ]
//
// On render it pulls EditContextScope.Current and registers an IFieldValidator that defers
// to System.ComponentModel.DataAnnotations.Validator. EditContext.AddValidator dedups by
// runtime type, so re-renders are idempotent. TagName is null — the component emits no DOM.
public sealed class DataAnnotationsValidator : Component
{
    // The validator reads mutable EditContext state via AddValidator, but its OWN output
    // (the empty Fragment) doesn't change between renders. Cache opt-out is unnecessary here
    // — re-rendering only re-registers the validator, which AddValidator no-ops on dedup.

    protected override Component? Render()
    {
        // Snapshot the render-scoped IServiceProvider at Render time. Handler invocation
        // (submit, change, blur) doesn't re-enter LiveRenderContext, so reading
        // LiveRenderContext.Current?.Services at validation time would return null and break
        // ValidationContext.GetService<T>(). The SP doesn't change across renders within a
        // session (same DI scope), so the first registration's snapshot is durable;
        // AddValidator's type-dedup discards subsequent re-registrations anyway.
        EditContextScope.Current?.AddValidator(new Inner(LiveRenderContext.Current?.Services));
        return null;
    }

    private sealed class Inner : IFieldValidator
    {
        private readonly IServiceProvider? _services;

        public Inner(IServiceProvider? services) => _services = services;

        // DataAnnotationsValidator reflects over the form model — when trimming, users must
        // preserve the public properties of every reachable model type (typically via
        // `[DynamicallyAccessedMembers]` on the binding source, or by referencing the model
        // from a [Route]'d page, which roots it via DynamicDependency). The graph walk
        // extends the same contract to nested sub-objects and collection items.
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "ValidationContext/Validator reflect over the model graph. The user-owned model " +
                            "types are preserved through binding annotations on their app, not Rask.Validation.DataAnnotations itself.")]
        public void Validate(EditContext context)
        {
            foreach (var node in ModelGraphWalker.Walk(context.Model))
            {
                ValidateNode(context, node);
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Same as Validate(): user-owned model preservation is the caller's responsibility.")]
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = "GetProperty on the field-owner's runtime type — preserved by the user's binding setup.")]
        public void ValidateField(EditContext context, FieldIdentifier field)
        {
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

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "TryValidateObject reflects over the node's runtime type — same trim contract " +
                            "as the root: the consumer is responsible for preserving every reachable model " +
                            "type's public properties, typically via [DynamicallyAccessedMembers].")]
        private void ValidateNode(EditContext context, object node)
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
                var members = r.MemberNames.ToList();
                if (members.Count == 0)
                {
                    context.AddValidationMessage(
                        new FieldIdentifier(node, string.Empty),
                        r.ErrorMessage ?? "Invalid value.");
                    continue;
                }

                foreach (var m in members)
                {
                    context.AddValidationMessage(
                        new FieldIdentifier(node, m),
                        r.ErrorMessage ?? "Invalid value.");
                }
            }
        }

        // ASP.NET Core / Blazor parity: ValidationContext is constructed with the render-scoped
        // IServiceProvider so custom ValidationAttribute subclasses can call
        // validationContext.GetService<T>() at validation time. _services is captured once at
        // Render() time (see comment on the outer Render method). Outside a live context (unit
        // tests that drive EditContext directly), _services is null and GetService<T>() returns
        // null, matching MVC's behavior when no service provider is configured.
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "ValidationContext's no-display-name ctor reflects for DisplayNameAttribute on the " +
                            "model — same constraint as Validate/ValidateField: the user-owned model type is " +
                            "preserved through the consumer's binding/page annotations.")]
        private ValidationContext NewValidationContext(object model) =>
            new(model, _services, null);
    }
}
