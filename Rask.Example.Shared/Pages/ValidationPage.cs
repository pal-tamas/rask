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
                "Set Form.Model to a class decorated with DataAnnotations. Rask auto-attaches a DataAnnotationsValidator and routes the submit through OnValidSubmit / OnInvalidSubmit. Per-field errors render through ValidationMessage; a top-of-form digest renders through ValidationSummary."),
            H2(Class: "h4 mt-4 mb-3")["Per-field — ValidationMessage"],
            CodeSample(
                """
                Form(Model: _model,
                     OnValidSubmit: (RegistrationModel m) =>
                         _submission = $"Registered: {m.Name} <{m.Email}>")[
                        Label(For: "name")["Name"],
                        Input(Bind: () => _model.Name, Id: "name"),
                        ValidationMessage(For: () => _model.Name,
                                          Class: "text-danger small"),
                        // ... Email, Age, Plan ...
                        Button(Type: "submit")["Register"]
                     ]
                """,
                Notes:
                "OnValidSubmit fires only after every [Required]/[EmailAddress]/[Range]/[StringLength] check passes. ValidationMessage subscribes to a single field via the same Bind-style expression.",
                Result: ValidationFieldsDemo()),
            H2(Class: "h4 mt-5 mb-3")["Top-of-form — ValidationSummary"],
            CodeSample(
                """
                Form(Model: _model,
                     OnValidSubmit: (RegistrationModel m) => /* ... */)[
                        ValidationSummary(Class: "alert alert-danger small"),
                        // ... unadorned fields, no per-field ValidationMessage ...
                        Button(Type: "submit")["Register"]
                     ]
                """,
                Notes:
                "ValidationSummary renders a <ul> of every current message in the form's EditContext. Pair it with novalidate-style inputs when you want a single error block instead of inline hints.",
                Result: ValidationSummaryDemo()),
            H2(Class: "h4 mt-5 mb-3")["Async — IAsyncFieldValidator"],
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

                _ctx = new EditContext(_model);
                _ctx.AddValidator(new DataAnnotationsValidator());
                _ctx.AddValidator(new UniqueUsernameValidator());

                Form(Model: _model, Context: _ctx, OnValidSubmit: ...)[
                    Input(Bind: () => _model.Username),
                    ValidatingIndicator(For: () => _model.Username)["Checking..."],
                    ValidationMessage(For: () => _model.Username)
                ]
                """,
                Notes:
                "Try \"admin\" or \"taken\" — the ValidatingIndicator shows while the 400ms check runs, then a message appears. Per-keystroke calls cancel any prior in-flight check (latest-wins). Submit awaits all validators before routing to OnValidSubmit vs OnInvalidSubmit. Registering an IAsyncFieldValidator makes the sync ctx.Validate() throw — go through the Form submit bridge (which already awaits internally).",
                Result: AsyncValidationDemo())
        ];
}
