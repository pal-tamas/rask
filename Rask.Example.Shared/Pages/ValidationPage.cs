using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("validation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ValidationPage : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Validation",
                "Set Form.Model to a class decorated with DataAnnotations. Rask auto-attaches a DataAnnotationsValidator and routes the submit through OnValidSubmit / OnInvalidSubmit. Per-field errors render through ValidationMessage; a top-of-form digest renders through ValidationSummary."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Per-field — ValidationMessage"]),
            Components.CodeSample(
                """
                Form(Model: _model,
                     OnValidSubmit: (RegistrationModel m) =>
                         _submission = $"Registered: {m.Name} <{m.Email}>",
                     Children: [
                        Label(For: "name", Children: ["Name"]),
                        Input(Bind: () => _model.Name, Id: "name"),
                        ValidationMessage(For: () => _model.Name,
                                          Class: "text-danger small"),
                        // ... Email, Age, Plan ...
                        Button(Type: "submit", Children: ["Register"])
                     ])
                """,
                Notes: "OnValidSubmit fires only after every [Required]/[EmailAddress]/[Range]/[StringLength] check passes. ValidationMessage subscribes to a single field via the same Bind-style expression.",
                Result: Components.ValidationFieldsDemo()),
            H2(Class: "h4 mt-5 mb-3", Children: ["Top-of-form — ValidationSummary"]),
            Components.CodeSample(
                """
                Form(Model: _model,
                     OnValidSubmit: (RegistrationModel m) => /* ... */,
                     Children: [
                        ValidationSummary(Class: "alert alert-danger small"),
                        // ... unadorned fields, no per-field ValidationMessage ...
                        Button(Type: "submit", Children: ["Register"])
                     ])
                """,
                Notes: "ValidationSummary renders a <ul> of every current message in the form's EditContext. Pair it with novalidate-style inputs when you want a single error block instead of inline hints.",
                Result: Components.ValidationSummaryDemo())
        );
}
