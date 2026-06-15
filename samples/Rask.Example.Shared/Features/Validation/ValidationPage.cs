using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("validation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ValidationPage : Component
{
    protected override RenderResult Head => Title()["Validation — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Validation",
            "Validators are opt-in components placed inside the Form. Drop DataAnnotationsValidator() in if your model uses [Required]/[Range]/etc., or FluentValidationValidator(...) if you wired up FluentValidation. Or skip both and pass Validate: directly on Form (cross-field rule) or on Input/Select/Textarea (per-field rule) — the callback is just a Func that returns IEnumerable<string>, sync or async."),
        H2(Class: "h4 mt-4 mb-3")["Per-field — DataAnnotations + ValidationMessage"],
        CodeSample(
            ["ValidationFieldsDemo.cs"],
            Notes:
            "DataAnnotationsValidator() is a real component — drop it in to opt into [Required]/[EmailAddress]/[Range]/[StringLength]. ValidationMessage's Template runs only when the field has at least one message — the empty state stays out of the DOM.",
            Result: ValidationFieldsDemo()),
        H2(Class: "h4 mt-5 mb-3")["Top-of-form — ValidationSummary"],
        CodeSample(
            ["ValidationSummaryDemo.cs"],
            Notes:
            "ValidationSummary's Template receives every current ValidationEntry (Field + Message). The component itself emits nothing — wrapper, list shape, and field formatting are entirely yours.",
            Result: ValidationSummaryDemo()),
        H2(Class: "h4 mt-5 mb-3")["Inline — Validate: on field & form"],
        CodeSample(
            ["InlineValidateDemo.cs"],
            Notes:
            "No DataAnnotations on the model — every rule lives at the call site. Validate: on Input runs per-keystroke (after the field is touched) and produces field-scoped messages. Validate: on Form runs at submit and produces summary-scoped messages. Both delegates accept either a Func<T, IEnumerable<string>> (sync) or Func<T, CancellationToken, ValueTask<IEnumerable<string>>> (async).",
            Result: InlineValidateDemo()),
        H2(Class: "h4 mt-5 mb-3")["Nested test"],
        CodeSample(
            ["NestedAsyncWithLiveTotalsDemo.cs"],
            Notes:
            "Combines async validation on a nested field with live derived UI. The Validate: lambda on Input(() => _model.Address.PostalCode, …) routes through the form's EditContext, same as a root field — latest-wins cancellation supersedes the prior in-flight check, ValidatingIndicator surfaces while the await is pending, and OperationCanceledException is swallowed without producing a generic message. Live calcs work because every event handler re-renders the owning component: string fields (DiscountCode) update on every keystroke via OnInput, numeric fields (Quantity / UnitPrice) update on blur via OnChange — both flow through the same dispatcher path. Submit only fires OnValidSubmit when every per-field rule passes; an undeliverable ZIP keeps the form pending until corrected.",
            Result: NestedAsyncWithLiveTotalsDemo()),
        H2(Class: "h4 mt-5 mb-3")["Inline async — Validate: as Func<T, CT, ValueTask<IEnumerable<string>>>"],
        CodeSample(
            ["InlineAsyncValidateDemo.cs"],
            Notes:
            "Same call-site shape as the sync Validate above — overload resolution picks the async variant from the lambda's two-parameter arity (or method-group conversion against a `(T, CancellationToken) -> ValueTask<IEnumerable<string>>` method). The ValidatingIndicator surfaces while the 250ms delay runs; rapid typing supersedes the prior in-flight check (latest-wins) and OperationCanceledException is swallowed without producing a generic message.",
            Result: InlineAsyncValidateDemo()),
        H2(Class: "h4 mt-5 mb-3")["Async — IAsyncFieldValidator / FluentValidation"],
        CodeSample(
            ["AsyncValidationDemo.cs"],
            Notes:
            "Try \"admin\" or \"taken\" — the ValidatingIndicator shows while the 400ms check runs, then a message appears. Per-keystroke calls cancel any prior in-flight check (latest-wins). Submit awaits all validators before routing to OnValidSubmit vs OnInvalidSubmit. The literal \"explode\" forces the demo validator to throw mid-await so you can see the generic \"Validation could not be completed.\" fallback that the framework surfaces when a validator faults. The indicator stays visible for ~200 ms after the check completes — EditContext.ValidatingStickyMs (default 200) smooths over sub-second validators that would otherwise flash on/off; set it to 0 on a manually-built EditContext to opt out.",
            Result: AsyncValidationDemo()),
        H2(Class: "h4 mt-5 mb-3")["Cross-field — Form-level Validate feeding ValidationSummary"],
        CodeSample(
            ["CrossFieldSummaryDemo.cs"],
            Notes:
            "Form-level Validate runs at submit time and adds its messages to FieldIdentifier(model, \"\") — they surface in ValidationSummary alongside any field-level messages but never tag a specific input.",
            Result: CrossFieldSummaryDemo()),
        H2(Class: "h4 mt-5 mb-3")["IValidatableObject — model-level Validate(ctx) alongside attributes"],
        CodeSample(
            ["ValidatableObjectDemo.cs"],
            Notes:
            "ASP.NET Core MVC accumulates attribute errors and IValidatableObject errors into the same ModelState — the BCL's own Validator.TryValidateObject silences IValidatableObject as soon as any attribute fails. DataAnnotationsValidator calls the interface directly so both layers always surface together. ValidationResult.MemberNames routes the message: an empty collection lands on FieldIdentifier(model, \"\") (ValidationSummary), a populated one tags a specific field (ValidationMessage). Submit empty to see Name's [Required] and the model's two Validate() results land in the same render.",
            Result: ValidatableObjectDemo()),
        H2(Class: "h4 mt-5 mb-3")["Programmatic — EditContext.Validate() and IsValidating"],
        CodeSample(
            ["ProgrammaticValidateDemo.cs"],
            Notes:
            "Hold your own EditContext and pass it via Context: on Form to drive validation from anywhere. Calling ctx.ValidateAsync() outside the submit path raises messages without routing through OnValidSubmit/OnInvalidSubmit. ctx.IsValidatingAny flips while async validators run — bind it to a button's Disabled to block submit during in-flight checks.",
            Result: ProgrammaticValidateDemo()),
        H2(Class: "h4 mt-5 mb-3")["FluentValidation — AbstractValidator<TModel>"],
        CodeSample(
            ["FluentValidationDemo.cs"],
            Notes:
            "FluentValidationValidator wraps any IValidator into an IAsyncFieldValidator. Per-keystroke runs use MemberNameValidatorSelector to scope FV to a single property; submit runs every rule on the model.",
            Result: FluentValidationDemo()),
        H2(Class: "h4 mt-5 mb-3")["First-error-wins — inline gates DataAnnotations"],
        CodeSample(
            ["FirstErrorWinsDemo.cs"],
            Notes:
            "The chain runs inline → form-level inline → sync IFieldValidator → async IAsyncFieldValidator. EditContext gates each later stage per-field: once any rule has flagged a field, the later rules on the SAME field stay quiet. Type nothing — only \"Code is required.\" shows. Type \"abc\" — the inline rule passes and DataAnnotations' \"Use the ABC-123 format.\" takes over. Type \"ABC-123\" — both pass and the form submits.",
            Result: FirstErrorWinsDemo()),
        H2(Class: "h4 mt-5 mb-3")["FluentValidation async — MustAsync inside the RuleFor chain"],
        CodeSample(
            ["FluentValidationAsyncDemo.cs"],
            Notes:
            "FluentValidation's own Cascade(CascadeMode.Stop) mirrors Rask's first-error-wins: NotEmpty must pass before Matches, which must pass before the MustAsync API check fires. Type \"TKT-001\" to see the indicator while the await is in flight, then the \"already reserved\" message land. Type a value not in the reserved set (e.g. \"TKT-999\") to submit successfully. FluentValidationValidator is registered as an IAsyncFieldValidator — async rules and sync rules share the one wrapper.",
            Result: FluentValidationAsyncDemo()),
        H2(Class: "h4 mt-5 mb-3")["Custom ValidationAttribute — IsValid, GetValidationResult, and DI"],
        CodeSample(
            ["CustomAttributeDemo.cs"],
            Notes:
            "Custom ValidationAttribute subclasses flow through DataAnnotationsValidator with no extra opt-in — System.ComponentModel.DataAnnotations.Validator walks every attribute on the property at validation time. ValidationContext is constructed with the render-scoped IServiceProvider, so attributes can resolve services via ctx.GetService<T>() the same way ASP.NET Core / Blazor's DataAnnotationsValidator do it. Try \"admin\" (NotBanned, DI-resolved), a weak password (StrongPassword.IsValid), or mismatched confirm (MatchesProperty reads ObjectInstance).",
            Result: CustomAttributeDemo())
    ];
}
