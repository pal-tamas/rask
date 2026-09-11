using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed partial class FloatingLabelsDemo : Component
{
    private static readonly (string? Value, string Text)[] Plans = [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private readonly AccountModel _model = new();
    private string? _submission;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Created account for {m.FullName} <{m.Email}>").Class("flex flex-col gap-2")[
            // One line per field. A labelled kit text field floats its label by default: the caption sits in
            // the field until there is content, then rises out of the way. The label is the field's real
            // <label>, linked to the control, and each bound field shows its own validation message, fed by
            // the [Required]/[Range]/etc. attributes through the built-in DataAnnotations pass. Every property
            // is nullable — Rask clears to null.
            UiInput.Bind(() => _model.FullName).Label("Full name").Id("ff-FullName"),
            UiInput.Bind(() => _model.Email).Label("Email address").Type(InputType.Email).Id("ff-Email"),
            UiInput.Bind(() => _model.Age).Label("Age").Id("ff-Age"),
            UiSelect.Bind(() => _model.Plan).Options(Plans).Label("Plan").Placeholder("— choose —").Id("ff-Plan"),
            UiTextarea.Bind(() => _model.Bio).Label("Short bio").Id("ff-Bio"),
            Div.Class("mt-1")[
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit)[UiIcon.Name(UiIconName.UserPlus), "Create account"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
    ];
}

public sealed class AccountModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Full name must be 2–60 characters.")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120.")]
    public int? Age { get; set; }

    [Required(ErrorMessage = "Pick a plan.")]
    public string? Plan { get; set; }

    [StringLength(200, ErrorMessage = "Bio must be 200 characters or fewer.")]
    public string? Bio { get; set; }
}
