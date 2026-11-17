using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Wasm.Fixture.Wasm;

/// <summary>
///     The signed-in user's background work in a browser half (#1348): the bell in the header, and the same work listed
///     in the page — both read from the server this app is a client of.
/// </summary>
[Route("/work")]
public sealed partial class WorkPage : Component
{
    protected override Component? Render() =>
        Div[
            Ui.Header[Ui.Spacer, Ui.WorkNotices],
            Ui.Main[Ui.WorkList]
        ];
}
