using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// Bootstrap form controls bound to a model with DataAnnotations validation. BsInput/BsSelect/BsCheck
// implement IFormControl<T>, so two-way binding, the .is-invalid styling and the .invalid-feedback
// message all come for free — no StateHasChanged on this surface. BsSelect renders a custom .dropdown-menu
// listbox by default (like BsMultiSelect / the pickers); Native: true drops to the plain OS <select>.
public sealed class BsFormsDemo : Component
{
    private static readonly string[] Plans = ["free", "pro", "team"];
    private static readonly int?[] Seats = [1, 2, 5, 10];

    private readonly Signup _model = new();
    private string? _result;

    private static string PlanLabel(string plan) => plan switch
    {
        "free" => "Free",
        "pro" => "Pro",
        "team" => "Team",
        _ => plan,
    };

    private static string SeatLabel(int? n) => $"{n} seat{(n == 1 ? "" : "s")}";

    protected override Component? Render() =>
    [
        Form<Signup>(_model, m => _result = $"Welcome, {m.Name}!", Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            BsInput(() => _model.Name, Label: "Name", Placeholder: "Jane Doe"),
            BsInput(() => _model.Email, Label: "Email", Type: InputType.Email, HelpText: "We never share it."),
            // Searchable: a Filter predicate adds a search field in the dropdown that narrows the options.
            BsSelect(() => _model.Plan, Plans, OptionLabel: p => Text(PlanLabel(p)),
                Filter: (p, t) => PlanLabel(p).Contains(t, StringComparison.OrdinalIgnoreCase),
                Placeholder: "— choose —", Label: "Plan", Floating: true, Id: "bs-plan"),
            // Nullable (int?) → an × clears it back to null; the null state shows the Placeholder.
            BsSelect(() => _model.Seats, Seats, OptionLabel: n => Text(SeatLabel(n)),
                Placeholder: "Any", Label: "Seats (optional)", Id: "bs-seats"),
            BsSelect(() => _model.Tier, Plans, OptionLabel: p => PlanLabel(p), Native: true,
                Label: "Tier (native <select>)", Id: "bs-tier"),
            BsCheck(() => _model.Agree, Switch: true, Label: "I accept the terms"),
            BsButton(Color: BsColor.Primary, Type: "submit")["Create account"]
        ],
        _result is null
            ? null
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

        // Second select bound in native mode (Native: true) — the plain OS <select>, no validation noise.
        public string Tier { get; set; } = "free";

        // Nullable, optional — a clearable (×) select that round-trips null.
        public int? Seats { get; set; }

        // Bound switch (left unvalidated — the [Range(typeof(bool), …)] "must accept" trick pulls in a
        // RequiresUnreferencedCode converter that breaks the trim-clean WASM publish).
        public bool Agree { get; set; }
    }
}
