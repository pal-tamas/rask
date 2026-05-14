using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("validation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ValidationPage : Component
{
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
                     Validate: (Func<LoginModel, IEnumerable<string>>)(m =>
                         m.Password == m.Confirm ? [] : ["Passwords do not match."]))[
                        Input(Bind: () => _model.Email,
                              Validate: (Func<string, IEnumerable<string>>)(v =>
                                  v.Contains('@') ? [] : ["Email looks wrong."])),
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
                    ValidatingIndicator(For: () => _model.Username)["Checking..."],
                    ValidationMessage(For: () => _model.Username, Template: FieldError)
                ]
                """,
                Notes:
                "Try \"admin\" or \"taken\" — the ValidatingIndicator shows while the 400ms check runs, then a message appears. Per-keystroke calls cancel any prior in-flight check (latest-wins). Submit awaits all validators before routing to OnValidSubmit vs OnInvalidSubmit. The literal \"explode\" forces the demo validator to throw mid-await so you can see the generic \"Validation could not be completed.\" fallback that the framework surfaces when a validator faults.",
                Result: AsyncValidationDemo()),
            H2(Class: "h4 mt-5 mb-3")["Cross-field — Form-level Validate feeding ValidationSummary"],
            CodeSample(
                """
                Form<TripModel>(_model, m => _submission = ...,
                     Validate: (Func<TripModel, IEnumerable<string>>)(m =>
                         m.Return > m.Depart
                             ? Array.Empty<string>()
                             : new[] { "Return date must be after departure." }))[
                        ValidationSummary(SummaryAlert),
                        Input(Bind: () => _model.Depart, Id: "v5-depart"),
                        Input(Bind: () => _model.Return, Id: "v5-return"),
                        Button(Type: "submit")["Book"]
                     ]
                """,
                Notes:
                "Form-level Validate runs at submit time and adds its messages to FieldIdentifier(model, \"\") — they surface in ValidationSummary alongside any field-level messages but never tag a specific input.",
                Result: CrossFieldSummaryDemo()),
            H2(Class: "h4 mt-5 mb-3")["Programmatic — EditContext.Validate() and IsValidating"],
            CodeSample(
                """
                _ctx = new EditContext(_model);
                _ctx.AddValidator(new SlowTitleValidator());   // 600ms async rule

                Form<TaskModel>(_model, m => _submission = ..., Context: _ctx)[
                    Input(Bind: () => _model.Title, Id: "v6-title"),
                    ValidatingIndicator(For: () => _model.Title)["Checking…"],
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
                          Validate: (Func<string, IEnumerable<string>>)(v =>
                              string.IsNullOrWhiteSpace(v)
                                  ? new[] { "Code is required." }
                                  : Array.Empty<string>())),
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
                    ValidatingIndicator(For: () => _model.Code)["Checking availability..."],
                    ValidationMessage(For: () => _model.Code, Template: FieldError),
                    Button(Type: "submit")["Reserve"]
                ]
                """,
                Notes:
                "FluentValidation's own Cascade(CascadeMode.Stop) mirrors Rask's first-error-wins: NotEmpty must pass before Matches, which must pass before the MustAsync API check fires. Type \"TKT-001\" to see the indicator while the await is in flight, then the \"already reserved\" message land. Type a value not in the reserved set (e.g. \"TKT-999\") to submit successfully. FluentValidationValidator is registered as an IAsyncFieldValidator — async rules and sync rules share the one wrapper.",
                Result: FluentValidationAsyncDemo())
        ];
}
