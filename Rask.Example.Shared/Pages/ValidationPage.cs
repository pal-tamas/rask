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
                Result: AsyncValidationDemo())
        ];
}
