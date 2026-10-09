using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK014 // routed through an explicit table: these pages have no generated entries
#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// A save page and the list it goes to afterwards. The list loads with two reads that pass no token, so each
// is cancelled with whatever the work in progress is when it starts.
internal sealed partial class ArrivalStubApp : Component
{
    private static readonly Route[] Pages = [Route.To<ArrivalStubForm>("/"), Route.To<ArrivalStubList>("/list")];

    protected override Component? HeadAssets => Title["arrival"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => Router.Routes(Pages);
}

[SkipFactory]
internal sealed class ArrivalStubForm : Component
{
    protected override Component? Render() =>
    [
        Button.OnClick(SaveThenGo)["save"],
        Button.OnClick(SaveGoAndCarryOn)["save and carry on"]
    ];

    // A save: its own async method, whose continuation carries the handler's flow onto the pool.
    private static async Task Save() => await Task.Delay(1).ConfigureAwait(false);

    private async Task SaveThenGo()
    {
        await Save();
        Go.To("/list");
    }

    private async Task SaveGoAndCarryOn()
    {
        await Save();
        Go.To("/list");
        await Save();
    }
}

[SkipFactory]
internal sealed class ArrivalStubList : Component
{
    private int? _rows;
    private int? _total;

    protected override async Task OnMount()
    {
        _rows = await Read();
        _total = await Read();
    }

    protected override Component? Render() => Span[_total is null ? "loading" : $"rows:{_rows} total:{_total}"];

    private static async Task<int> Read()
    {
        var token = Current.Cancellation;
        await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return 3;
    }
}
