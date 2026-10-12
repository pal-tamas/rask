using System.ComponentModel.DataAnnotations;
using Rask.Core;
using Rask.Core.Forms;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A form as an app writes it — a fixed list of children, the kit's toasts beside it — whose save throws by
///     the name it is given, and whose store says "p" is taken both when the field is left and when it is saved.
/// </summary>
public sealed partial class SaveFaultApp : Component
{
    public const string Taken = "Ilyen néven már létezik viszonylat.";
    public const string Sentence = "A név nem lehet hosszabb 50 karakternél.";

    private readonly Destination _model = new();
    private readonly EditContext _form;

    public SaveFaultApp()
    {
        _form = new EditContext(_model) { ValidatingStickyMs = 0 };
        _form.AddValidator(new TakenName());
    }

    protected override Component? HeadAssets => Markup.Title["save-fault"];

    protected override Component? Render() =>
    [
        Main[
            Form.Model(_model).Context(_form).OnSubmit(Save)[
                Input.Bind(() => _model.Name).Id("name"),
                Validation.Message.Template(static messages => P.Id("name-error")[messages[0]]).For(() => _model.Name),
                Button.Type(ButtonType.Submit).Id("save")["Mentés"]
            ]
        ],
        Ui.Toast,
    ];

    private static async Task Save(Destination destination)
    {
        await Task.Delay(1);
        throw destination.Name switch
        {
            "tenant" => new MissingTenantException(),
            "rule" => new ValidationException(Sentence),
            _ => new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'."),
        };
    }

    public sealed class Destination
    {
        public string Name { get; set; } = "";
    }

    public sealed class MissingTenantException() : Exception("No tenant is selected for partner 4114");

    // The store's answer to a name it already holds: asked for the field when it is left, and for the form at a save.
    private sealed class TakenName : IAsyncFieldValidator
    {
        public async ValueTask Validate(EditContext context, CancellationToken ct)
        {
            await Task.Delay(5, ct).ConfigureAwait(false);
            Say(context, new FieldIdentifier(context.Model, nameof(Destination.Name)));
        }

        public async ValueTask ValidateField(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            await Task.Delay(5, ct).ConfigureAwait(false);
            Say(context, field);
        }

        private static void Say(EditContext context, FieldIdentifier field)
        {
            if (context.Model is Destination { Name: "p" })
            {
                context.AddValidationMessage(field, Taken);
            }
        }
    }
}
