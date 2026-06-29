using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// Bootstrap form controls bound to a model with DataAnnotations validation. BsInput/BsSelect/BsCheck
// implement IFormControl<T>, so two-way binding, the .is-invalid styling and the .invalid-feedback
// message all come for free — no StateHasChanged on this surface.
public sealed class BsFormsDemo : Component
{
    private readonly Signup _model = new();
    private string? _result;

    protected override RenderResult Render() =>
    [
        Form<Signup>(_model, m => _result = $"Welcome, {m.Name}!", Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            BsInput(() => _model.Name, Label: "Name", Placeholder: "Jane Doe"),
            BsInput(() => _model.Email, Label: "Email", Type: InputType.Email, HelpText: "We never share it."),
            BsSelect(() => _model.Plan, Label: "Plan")[
                Option("")["— choose —"],
                Option("free")["Free"],
                Option("pro")["Pro"],
                Option("team")["Team"]
            ],
            BsCheck(() => _model.Agree, Switch: true, Label: "I accept the terms"),
            BsButton(Color: BsColor.Primary, Type: "submit")["Create account"]
        ],
        _result is null
            ? Fragment()
            : BsAlert(Color: BsColor.Success, Class: "mt-3 mb-0")[
                BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _result]
    ];

    private sealed class Signup
    {
        [Required]
        public string Name { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Please choose a plan.")]
        public string Plan { get; set; } = "";

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms.")]
        public bool Agree { get; set; }
    }
}
