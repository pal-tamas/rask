using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("validation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ValidationPage : Component
{
    protected override Component? Head => Title()["Validation — Rask"];

    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                "Validation",
                "Validators are opt-in components placed inside the Form. Drop DataAnnotationsValidator() in if your model uses [Required]/[Range]/etc., or FluentValidationValidator(...) if you wired up FluentValidation. Or skip both and pass Validate: directly on Form (cross-field rule) or on Input/Select/Textarea (per-field rule) — the callback is just a Func that returns IEnumerable<string>, sync or async."),
            H2(Class: "h4 mt-4 mb-3")["Per-field — DataAnnotations + ValidationMessage"],
            CodeSample(
                """
                static Component FieldError(IReadOnlyList<string> msgs) =>
                    Fragment()[msgs.Select(m => (Child)Div(Class: "text-danger small")[m])];

                Form(Model: _model,
                     OnValidSubmit: (RegistrationModel m) =>
                         _submission = $"Registered: {m.Name} <{m.Email}>")[
                        DataAnnotationsValidator(),
                        Label(For: "name")["Name"],
                        Input(Bind: () => _model.Name, Id: "name"),
                        ValidationMessage(For: () => _model.Name, Template: FieldError),
                        // ... Email, Age, Plan ...
                        Button(Type: "submit")["Register"]
                     ]
                """,
                Notes:
                "DataAnnotationsValidator() is a real component — drop it in to opt into [Required]/[EmailAddress]/[Range]/[StringLength]. ValidationMessage's Template runs only when the field has at least one message — the empty state stays out of the DOM.",
                Result: ValidationFieldsDemo()),
            H2(Class: "h4 mt-5 mb-3")["Top-of-form — ValidationSummary"],
            CodeSample(
                """
                static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
                    Div(Class: "alert alert-danger small mb-0")[
                        Ul(Class: "mb-0 ps-3")[
                            entries.Select(e => (Child)Li()[
                                Strong()[e.Field], ": ", e.Message
                            ])
                        ]
                    ];

                Form(Model: _model,
                     OnValidSubmit: (RegistrationModel m) => /* ... */)[
                        DataAnnotationsValidator(),
                        ValidationSummary(Template: SummaryAlert),
                        // ... unadorned fields, no per-field ValidationMessage ...
                        Button(Type: "submit")["Register"]
                     ]
                """,
                Notes:
                "ValidationSummary's Template receives every current ValidationEntry (Field + Message). The component itself emits nothing — wrapper, list shape, and field formatting are entirely yours.",
                Result: ValidationSummaryDemo()),
            H2(Class: "h4 mt-5 mb-3")["Inline — Validate: on field & form"],
            CodeSample(
                """
                Form<LoginModel>(_model,
                     OnValidSubmit: m => _submission = "Welcome",
                     Validate: m =>
                         m.Password == m.Confirm ? [] : ["Passwords do not match."])[
                        Input(Bind: () => _model.Email,
                              Validate: v =>
                                  v.Contains('@') ? [] : ["Email looks wrong."]),
                        ValidationMessage(For: () => _model.Email, Template: FieldError),
                        Input(Bind: () => _model.Password, Type: "password"),
                        Input(Bind: () => _model.Confirm, Type: "password"),
                        ValidationSummary(Template: SummaryAlert),
                        Button(Type: "submit")["Sign in"]
                     ]
                """,
                Notes:
                "No DataAnnotations on the model — every rule lives at the call site. Validate: on Input runs per-keystroke (after the field is touched) and produces field-scoped messages. Validate: on Form runs at submit and produces summary-scoped messages. Both delegates accept either a Func<T, IEnumerable<string>> (sync) or Func<T, CancellationToken, ValueTask<IEnumerable<string>>> (async).",
                Result: InlineValidateDemo()),
            H2(Class: "h4 mt-5 mb-3")["Nested test"],
            CodeSample(
                """
                public sealed class StorefrontModel {
                    public string CustomerName { get; set; } = "";
                    public StorefrontAddress Address { get; set; } = new();
                    public List<StorefrontLineItem> Items { get; set; } = new();
                    public string DiscountCode { get; set; } = "";
                }

                static async ValueTask<IEnumerable<string>> ValidatePostalAsync(
                    string code, CancellationToken ct)
                {
                    if (!Regex.IsMatch(code ?? "", @"^\d{5}$"))
                        return new[] { "Postal code must be 5 digits." };
                    await Task.Delay(300, ct);
                    return Undeliverable.Contains(code)
                        ? new[] { "We don't ship to this area." }
                        : Array.Empty<string>();
                }

                protected override Component Render() {
                    // Derived state — recomputed on every render. The dispatcher re-renders
                    // after each handler completes, so the figures stay in sync automatically.
                    var subtotal = _model.Items.Sum(i => i.Quantity * i.UnitPrice);
                    var pct = PromoCodes.GetValueOrDefault(_model.DiscountCode, 0m);
                    var total = subtotal * (1m - pct) * 1.08m;

                    return Form<StorefrontModel>(_model, OnValidSubmit)[
                        // Async validation on a NESTED field.
                        Input(Bind: () => _model.Address.PostalCode, Validate: ValidatePostalAsync),
                        ValidatingIndicator(For: () => _model.Address.PostalCode, Template: Checking),
                        ValidationMessage(For: () => _model.Address.PostalCode, Template: FieldError),
                        Input(Bind: () => _model.Items[0].Quantity),
                        Input(Bind: () => _model.Items[0].UnitPrice),
                        Input(Bind: () => _model.DiscountCode),
                        Div()[ "Total ", total ]
                    ];
                }
                """,
                Notes:
                "Combines async validation on a nested field with live derived UI. The Validate: lambda on Input(() => _model.Address.PostalCode, …) routes through the form's EditContext, same as a root field — latest-wins cancellation supersedes the prior in-flight check, ValidatingIndicator surfaces while the await is pending, and OperationCanceledException is swallowed without producing a generic message. Live calcs work because every event handler re-renders the owning component: string fields (DiscountCode) update on every keystroke via OnInput, numeric fields (Quantity / UnitPrice) update on blur via OnChange — both flow through the same dispatcher path. Submit only fires OnValidSubmit when every per-field rule passes; an undeliverable ZIP keeps the form pending until corrected.",
                Result: NestedAsyncWithLiveTotalsDemo()),
            H2(Class: "h4 mt-5 mb-3")["Inline async — Validate: as Func<T, CT, ValueTask<IEnumerable<string>>>"],
            CodeSample(
                """
                static async ValueTask<IEnumerable<string>> CheckCodeAsync(string code, CancellationToken ct) {
                    await Task.Delay(250, ct);
                    return TakenCodes.Contains(code) ? new[] { $"\"{code}\" is reserved." } : [];
                }

                Form<PromoModel>(Model: _model, OnValidSubmit: m => ...,
                     Validate: async (m, ct) =>
                         string.IsNullOrWhiteSpace(m.Code) ? ["Code is required."] : [])[
                        Input(Bind: () => _model.Code, Validate: CheckCodeAsync),
                        ValidatingIndicator(For: () => _model.Code, Template: Checking),
                        ValidationMessage(For: () => _model.Code, Template: FieldError),
                        Button(Type: "submit")["Redeem"]
                     ]
                """,
                Notes:
                "Same call-site shape as the sync Validate above — overload resolution picks the async variant from the lambda's two-parameter arity (or method-group conversion against a `(T, CancellationToken) -> ValueTask<IEnumerable<string>>` method). The ValidatingIndicator surfaces while the 250ms delay runs; rapid typing supersedes the prior in-flight check (latest-wins) and OperationCanceledException is swallowed without producing a generic message.",
                Result: InlineAsyncValidateDemo()),
            H2(Class: "h4 mt-5 mb-3")["Async — IAsyncFieldValidator / FluentValidation"],
            CodeSample(
                """
                public sealed class UniqueUsernameValidator : IAsyncFieldValidator {
                    public async ValueTask ValidateFieldAsync(
                        EditContext ctx, FieldIdentifier field, CancellationToken ct) {
                            await Task.Delay(400, ct);   // pretend it's an API call
                            if (Taken.Contains(ctx.Model.Username))
                                ctx.AddValidationMessage(field, "Already taken.");
                    }
                    /* ValidateAsync similar */
                }

                Form(Model: _model, OnValidSubmit: ...)[
                    DataAnnotationsValidator(),
                    new UniqueUsernameValidator() /* or FluentValidationValidator(new …) */,
                    Input(Bind: () => _model.Username),
                    ValidatingIndicator(For: () => _model.Username, Template: Checking),
                    ValidationMessage(For: () => _model.Username, Template: FieldError)
                ]
                """,
                Notes:
                "Try \"admin\" or \"taken\" — the ValidatingIndicator shows while the 400ms check runs, then a message appears. Per-keystroke calls cancel any prior in-flight check (latest-wins). Submit awaits all validators before routing to OnValidSubmit vs OnInvalidSubmit. The literal \"explode\" forces the demo validator to throw mid-await so you can see the generic \"Validation could not be completed.\" fallback that the framework surfaces when a validator faults. The indicator stays visible for ~200 ms after the check completes — EditContext.ValidatingStickyMs (default 200) smooths over sub-second validators that would otherwise flash on/off; set it to 0 on a manually-built EditContext to opt out.",
                Result: AsyncValidationDemo()),
            H2(Class: "h4 mt-5 mb-3")["Cross-field — Form-level Validate feeding ValidationSummary"],
            CodeSample(
                """
                Form<TripModel>(Model: _model, OnValidSubmit: m => _submission = ...,
                     Validate: m =>
                         m.Return > m.Depart
                             ? Array.Empty<string>()
                             : new[] { "Return date must be after departure." })[
                        ValidationSummary(SummaryAlert),
                        Input(Bind: () => _model.Depart, Id: "v5-depart"),
                        Input(Bind: () => _model.Return, Id: "v5-return"),
                        Button(Type: "submit")["Book"]
                     ]
                """,
                Notes:
                "Form-level Validate runs at submit time and adds its messages to FieldIdentifier(model, \"\") — they surface in ValidationSummary alongside any field-level messages but never tag a specific input.",
                Result: CrossFieldSummaryDemo()),
            H2(Class: "h4 mt-5 mb-3")["IValidatableObject — model-level Validate(ctx) alongside attributes"],
            CodeSample(
                """
                public sealed class BookingModel : IValidatableObject {
                    [Required(ErrorMessage = "Name is required.")]
                    public string Name { get; set; } = "";
                    public DateOnly Departure { get; set; }
                    public DateOnly Arrival { get; set; }

                    public IEnumerable<ValidationResult> Validate(ValidationContext ctx) {
                        if (Departure < Today)
                            yield return new ValidationResult(
                                "Departure cannot be in the past.",
                                new[] { nameof(Departure) });   // per-field
                        if (Arrival <= Departure)
                            yield return new ValidationResult(
                                "Arrival must be after departure.");   // form-level
                    }
                }

                Form<BookingModel>(_model, OnValidSubmit: m => /* ... */)[
                    DataAnnotationsValidator(),
                    ValidationSummary(SummaryAlert),
                    Input(Bind: () => _model.Name, Id: "v11-name"),
                    ValidationMessage(For: () => _model.Name, Template: FieldError),
                    Input(Bind: () => _model.Departure, Id: "v11-departure"),
                    ValidationMessage(For: () => _model.Departure, Template: FieldError),
                    Input(Bind: () => _model.Arrival, Id: "v11-arrival"),
                    Button(Type: "submit")["Book"]
                ]
                """,
                Notes:
                "ASP.NET Core MVC accumulates attribute errors and IValidatableObject errors into the same ModelState — the BCL's own Validator.TryValidateObject silences IValidatableObject as soon as any attribute fails. DataAnnotationsValidator calls the interface directly so both layers always surface together. ValidationResult.MemberNames routes the message: an empty collection lands on FieldIdentifier(model, \"\") (ValidationSummary), a populated one tags a specific field (ValidationMessage). Submit empty to see Name's [Required] and the model's two Validate() results land in the same render.",
                Result: ValidatableObjectDemo()),
            H2(Class: "h4 mt-5 mb-3")["Programmatic — EditContext.Validate() and IsValidating"],
            CodeSample(
                """
                _ctx = new EditContext(_model);
                _ctx.AddValidator(new SlowTitleValidator());   // 600ms async rule

                Form<TaskModel>(_model, m => _submission = ..., Context: _ctx)[
                    Input(Bind: () => _model.Title, Id: "v6-title"),
                    ValidatingIndicator(For: () => _model.Title, Template: Checking),
                    ValidationMessage(For: () => _model.Title, Template: FieldError),
                    Button(Type: "button", Id: "v6-validate-now",
                           OnClickAsync: () => _ctx.ValidateAsync().AsTask())["Validate now"],
                    Button(Type: "submit", Id: "v6-submit",
                           Disabled: _ctx.IsValidatingAny)["Save"]
                ]
                """,
                Notes:
                "Hold your own EditContext and pass it via Context: on Form to drive validation from anywhere. Calling ctx.ValidateAsync() outside the submit path raises messages without routing through OnValidSubmit/OnInvalidSubmit. ctx.IsValidatingAny flips while async validators run — bind it to a button's Disabled to block submit during in-flight checks.",
                Result: ProgrammaticValidateDemo()),
            H2(Class: "h4 mt-5 mb-3")["FluentValidation — AbstractValidator<TModel>"],
            CodeSample(
                """
                public sealed class OrderValidator : AbstractValidator<OrderModel> {
                    public OrderValidator() {
                        RuleFor(x => x.Product).NotEmpty();
                        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1)
                            .WithMessage("Quantity must be at least 1.");
                    }
                }

                Form<OrderModel>(_model, m => _submission = ...)[
                    FluentValidationValidator(new OrderValidator()),
                    Input(Bind: () => _model.Product, Id: "v7-product"),
                    ValidationMessage(For: () => _model.Product, Template: FieldError),
                    Input(Bind: () => _model.Quantity, Id: "v7-quantity"),
                    ValidationMessage(For: () => _model.Quantity, Template: FieldError),
                    Button(Type: "submit")["Order"]
                ]
                """,
                Notes:
                "FluentValidationValidator wraps any IValidator into an IAsyncFieldValidator. Per-keystroke runs use MemberNameValidatorSelector to scope FV to a single property; submit runs every rule on the model.",
                Result: FluentValidationDemo()),
            H2(Class: "h4 mt-5 mb-3")["First-error-wins — inline gates DataAnnotations"],
            CodeSample(
                """
                public sealed class LicenseModel {
                    [RegularExpression(@"^[A-Z]{3}-\d{3}$",
                        ErrorMessage = "Use the ABC-123 format.")]
                    public string Code { get; set; } = "";
                }

                Form<LicenseModel>(_model, m => _submission = "Activated")[
                    DataAnnotationsValidator(),
                    Input(Bind: () => _model.Code,
                          Validate: v =>
                              string.IsNullOrWhiteSpace(v)
                                  ? new[] { "Code is required." }
                                  : Array.Empty<string>()),
                    ValidationMessage(For: () => _model.Code, Template: FieldError),
                    Button(Type: "submit")["Activate"]
                ]
                """,
                Notes:
                "The chain runs inline → form-level inline → sync IFieldValidator → async IAsyncFieldValidator. EditContext gates each later stage per-field: once any rule has flagged a field, the later rules on the SAME field stay quiet. Type nothing — only \"Code is required.\" shows. Type \"abc\" — the inline rule passes and DataAnnotations' \"Use the ABC-123 format.\" takes over. Type \"ABC-123\" — both pass and the form submits.",
                Result: FirstErrorWinsDemo()),
            H2(Class: "h4 mt-5 mb-3")["FluentValidation async — MustAsync inside the RuleFor chain"],
            CodeSample(
                """
                public sealed class TicketValidator : AbstractValidator<TicketModel> {
                    public TicketValidator() {
                        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
                            .NotEmpty().WithMessage("Code is required.")
                            .Matches(@"^TKT-\d{3}$").WithMessage("Format must be TKT-123.")
                            .MustAsync(async (code, ct) => {
                                await Task.Delay(400, ct); // pretend it's an API
                                return !Reserved.Contains(code);
                            }).WithMessage("Code is already reserved.");
                    }
                }

                Form<TicketModel>(_model, m => _submission = ...)[
                    FluentValidationValidator(new TicketValidator()),
                    Input(Bind: () => _model.Code),
                    ValidatingIndicator(For: () => _model.Code, Template: Checking),
                    ValidationMessage(For: () => _model.Code, Template: FieldError),
                    Button(Type: "submit")["Reserve"]
                ]
                """,
                Notes:
                "FluentValidation's own Cascade(CascadeMode.Stop) mirrors Rask's first-error-wins: NotEmpty must pass before Matches, which must pass before the MustAsync API check fires. Type \"TKT-001\" to see the indicator while the await is in flight, then the \"already reserved\" message land. Type a value not in the reserved set (e.g. \"TKT-999\") to submit successfully. FluentValidationValidator is registered as an IAsyncFieldValidator — async rules and sync rules share the one wrapper.",
                Result: FluentValidationAsyncDemo()),
            H2(Class: "h4 mt-5 mb-3")["Custom ValidationAttribute — IsValid, GetValidationResult, and DI"],
            CodeSample(
                """
                // IsValid(object?) — the simplest custom-attribute shape.
                public sealed class StrongPasswordAttribute : ValidationAttribute {
                    public override bool IsValid(object? value) =>
                        value is string s && s.Length >= 8
                            && s.Any(char.IsLetter) && s.Any(char.IsDigit);
                }

                // GetValidationResult — reads ValidationContext.ObjectInstance for cross-field rules.
                public sealed class MatchesPropertyAttribute(string otherProperty) : ValidationAttribute {
                    protected override ValidationResult? IsValid(object? value, ValidationContext ctx) {
                        var other = ctx.ObjectInstance.GetType().GetProperty(otherProperty)
                            ?.GetValue(ctx.ObjectInstance);
                        return Equals(value, other)
                            ? ValidationResult.Success
                            : new ValidationResult(ErrorMessage, new[] { ctx.MemberName! });
                    }
                }

                // ValidationContext.GetService<T>() — resolves services from the render-scoped SP.
                public sealed class NotBannedAttribute : ValidationAttribute {
                    protected override ValidationResult? IsValid(object? value, ValidationContext ctx) {
                        var svc = (IBannedWordService?)ctx.GetService(typeof(IBannedWordService));
                        return svc is { } s && value is string str && s.Words.Contains(str)
                            ? new ValidationResult(FormatErrorMessage(str), new[] { ctx.MemberName! })
                            : ValidationResult.Success;
                    }
                }

                public sealed class CustomAttributeModel {
                    [Required, NotBanned(ErrorMessage = "\"{0}\" isn't available.")]
                    public string Username { get; set; } = "";

                    [Required, StrongPassword(ErrorMessage = "8+ chars, letters and digits.")]
                    public string Password { get; set; } = "";

                    [Required, MatchesProperty(nameof(Password), ErrorMessage = "Passwords don't match.")]
                    public string ConfirmPassword { get; set; } = "";
                }

                // Program.cs: services.AddSingleton<IBannedWordService, BannedWordService>();
                """,
                Notes:
                "Custom ValidationAttribute subclasses flow through DataAnnotationsValidator with no extra opt-in — System.ComponentModel.DataAnnotations.Validator walks every attribute on the property at validation time. ValidationContext is constructed with the render-scoped IServiceProvider, so attributes can resolve services via ctx.GetService<T>() the same way ASP.NET Core / Blazor's DataAnnotationsValidator do it. Try \"admin\" (NotBanned, DI-resolved), a weak password (StrongPassword.IsValid), or mismatched confirm (MatchesProperty reads ObjectInstance).",
                Result: CustomAttributeDemo())
        ];
}
