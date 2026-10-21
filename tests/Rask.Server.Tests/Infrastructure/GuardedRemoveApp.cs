using Microsoft.AspNetCore.Authorization;
using Rask.Core;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A list whose remove only an admin may run, handed to its buttons both as a method group and from a lambda.
/// </summary>
public sealed partial class GuardedRemoveApp : Component
{
    private int _removed;

    protected override Component? HeadAssets => Markup.Title["guarded-remove"];

    protected override Component? Render() =>
    [
        Main[
            P.Id("removed")[$"removed={_removed}"],
            Button.Id("by-method-group").OnClick(Remove)["Remove"],
            Button.Id("by-lambda").OnClick(async () => await Remove())["Remove"]
        ],
        Ui.Toast,
    ];

    [Authorize(Roles = "admin")]
    private async Task Remove()
    {
        await Task.Delay(1);
        _removed++;
    }
}
