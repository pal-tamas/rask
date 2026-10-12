using Rask.Core;
using Rask.Cqrs;
using Rask.Wire;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A list as an app writes it — a row, a bin, the kit's toasts beside it — whose delete the store refuses
///     in a sentence of its own, or fails.
/// </summary>
public sealed partial class RefusedDeleteApp : Component
{
    public const string Sentence = "A pótkocsi nem törölhető, mert menetlevél hivatkozik rá.";

    private int _attempts;

    protected override Component? HeadAssets => Markup.Title["refused-delete"];

    protected override Component? Render() =>
    [
        Main[
            P.Id("attempts")[$"attempts={_attempts}"],
            Ul[Li["ABC-123"]],
            Button.Id("delete").OnClick(Delete)["Törlés"],
            Button.Id("fail").OnClick(Fail)["Hiba"]
        ],
        Ui.Toast,
    ];

    private async Task Delete()
    {
        _attempts++;
        await Task.Delay(1);
        throw new RaskValidationException([new FieldFailure(Sentence, [], Source: "FK_Waybill_Trailer")]);
    }

    private async Task Fail()
    {
        _attempts++;
        await Task.Delay(1);
        throw new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'.");
    }
}
