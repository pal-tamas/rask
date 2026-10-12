using System.ComponentModel.DataAnnotations;
using Rask.Core;
using Rask.Core.Forms;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A form as an app writes it — a fixed list of children, the kit's toasts beside it — whose save throws by
///     the name it is given.
/// </summary>
public sealed partial class SaveFaultApp : Component
{
    public const string Sentence = "A név nem lehet hosszabb 50 karakternél.";

    private readonly Destination _model = new();
    private readonly EditContext _form;

    public SaveFaultApp() => _form = new EditContext(_model) { ValidatingStickyMs = 0 };

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
}
