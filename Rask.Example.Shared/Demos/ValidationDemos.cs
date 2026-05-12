using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Demos;

public sealed class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    protected override Component Render() =>
        Fragment(
            Form<RegistrationModel>(
                _model,
                OnValidSubmit: m => _submission = $"Registered: {m.Name} <{m.Email}>",
                Class: "vstack gap-3",
                Children:
                [
                    Div(Children:
                    [
                        Label(For: "v1-name", Class: "form-label small mb-1",
                            Children: ["Name"]),
                        Input(Bind: () => _model.Name, Id: "v1-name", Class: "form-control"),
                        ValidationMessage(For: () => _model.Name, Class: "text-danger small mt-1")
                    ]),
                    Div(Children:
                    [
                        Label(For: "v1-email", Class: "form-label small mb-1",
                            Children: ["Email"]),
                        Input(Bind: () => _model.Email, Id: "v1-email", Type: "email",
                            Class: "form-control"),
                        ValidationMessage(For: () => _model.Email, Class: "text-danger small mt-1")
                    ]),
                    Div(Children:
                    [
                        Label(For: "v1-age", Class: "form-label small mb-1",
                            Children: ["Age"]),
                        Input(Bind: () => _model.Age, Id: "v1-age", Class: "form-control"),
                        ValidationMessage(For: () => _model.Age, Class: "text-danger small mt-1")
                    ]),
                    Div(Children:
                    [
                        Label(For: "v1-plan", Class: "form-label small mb-1",
                            Children: ["Plan"]),
                        Select(Bind: () => _model.Plan, Id: "v1-plan", Class: "form-select",
                            Children:
                            [
                                Option("", Children: ["— choose —"]),
                                Option("free", Children: ["Free"]),
                                Option("pro", Children: ["Pro"]),
                                Option("team", Children: ["Team"])
                            ]),
                        ValidationMessage(For: () => _model.Plan, Class: "text-danger small mt-1")
                    ]),
                    Div(Children:
                    [
                        Button("submit", Class: "btn btn-primary",
                            Children: [I(Class: "bi bi-check2-circle me-1"), "Register"])
                    ])
                ]),
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0",
                    Children: [I(Class: "bi bi-check-circle me-2"), _submission]));
}

public sealed class ValidationSummaryDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    protected override Component Render() =>
        Fragment(
            Form<RegistrationModel>(
                _model,
                OnValidSubmit: m => _submission = $"Registered: {m.Name} <{m.Email}>",
                Class: "vstack gap-3",
                Children:
                [
                    ValidationSummary(Class: "alert alert-danger small mb-0"),
                    Div(Children:
                    [
                        Label(For: "v2-name", Class: "form-label small mb-1",
                            Children: ["Name"]),
                        Input(Bind: () => _model.Name, Id: "v2-name", Class: "form-control")
                    ]),
                    Div(Children:
                    [
                        Label(For: "v2-email", Class: "form-label small mb-1",
                            Children: ["Email"]),
                        Input(Bind: () => _model.Email, Id: "v2-email", Type: "email",
                            Class: "form-control")
                    ]),
                    Div(Children:
                    [
                        Label(For: "v2-age", Class: "form-label small mb-1",
                            Children: ["Age"]),
                        Input(Bind: () => _model.Age, Id: "v2-age", Class: "form-control")
                    ]),
                    Div(Children:
                    [
                        Label(For: "v2-plan", Class: "form-label small mb-1",
                            Children: ["Plan"]),
                        Select(Bind: () => _model.Plan, Id: "v2-plan", Class: "form-select",
                            Children:
                            [
                                Option("", Children: ["— choose —"]),
                                Option("free", Children: ["Free"]),
                                Option("pro", Children: ["Pro"]),
                                Option("team", Children: ["Team"])
                            ])
                    ]),
                    Div(Children:
                    [
                        Button("submit", Class: "btn btn-primary",
                            Children: [I(Class: "bi bi-check2-circle me-1"), "Register"])
                    ])
                ]),
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0",
                    Children: [I(Class: "bi bi-check-circle me-2"), _submission]));
}

public sealed class RegistrationModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "Name must be 2–40 characters.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Range(13, 120, ErrorMessage = "Age must be between 13 and 120.")]
    public int Age { get; set; }

    [Required(ErrorMessage = "Pick a plan.")]
    public string Plan { get; set; } = "";
}
