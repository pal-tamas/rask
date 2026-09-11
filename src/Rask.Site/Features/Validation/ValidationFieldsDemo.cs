using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class ValidationFieldsDemo : Component
{
    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("flex flex-col gap-3")[
            Div[
                Label.For("v1-name").Class($"{Tw.Label} text-sm mb-1")["Name"],
                Input.Bind(() => _model.Name).Id("v1-name").Class(Tw.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                Label.For("v1-email").Class($"{Tw.Label} text-sm mb-1")["Email"],
                Input.Bind(() => _model.Email)
                    .Id("v1-email")
                    .Type(InputType.Email)
                    .Class(Tw.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Email)
            ],
            Div[
                Label.For("v1-age").Class($"{Tw.Label} text-sm mb-1")["Age"],
                Input.Bind(() => _model.Age).Id("v1-age").Class(Tw.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Age)
            ],
            Div[
                Label.For("v1-plan").Class($"{Tw.Label} text-sm mb-1")["Plan"],
                Select.Bind(() => _model.Plan).Id("v1-plan").Class(Tw.Select)[
                    Option.Value("")["— choose —"],
                    Option.Value("free")["Free"],
                    Option.Value("pro")["Pro"],
                    Option.Value("team")["Team"]
                ],
                ValidationMessage.Template(FieldError).For(() => _model.Plan)
            ],
            Div[
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit)[UiIcon.Name(UiIconName.CheckCircle), "Register"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
    ];
}
