using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// A save page and the list it goes to afterwards, under a layout that outlives both. The list loads with two
// reads that pass no token, as `await Product.Where(…)` then `await Product.Count()` do — so each is cancelled
// with whatever the work in progress is when it starts.
public sealed partial class ArrivalApp : Component
{
    protected override Component? HeadAssets => Title["arrival"];

    protected override Component? Render() => Router;
}

internal static class ArrivalData
{
    // A save: its own async method, whose continuation carries the handler's flow onto the pool.
    public static async Task Save() => await Task.Delay(1).ConfigureAwait(false);

    public static async Task<int> Read()
    {
        var token = Current.Cancellation;
        await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return 3;
    }
}

[Route("/arrival")]
public sealed partial class ArrivalLayout : Component
{
    private int _kept;

    protected override Component? Render() =>
        Div[
            Button.Id("keep").OnClick(SaveFromTheLayout)["keep"],
            Span.Id("kept")[$"kept:{_kept}"],
            NavLink.Href(Routes.ArrivalListPage()).Id("to-list")["list"],
            Outlet];

    // The layout is not unmounted by the navigation it starts, so its handler runs on to the end.
    private async Task SaveFromTheLayout()
    {
        await ArrivalData.Save();
        Routes.ArrivalListPage().Go();
        _kept += await ArrivalData.Read();
    }
}

[Route("form")]
[ParentRoute(typeof(ArrivalLayout))]
public sealed partial class ArrivalFormPage : Component
{
    protected override Component? Render() =>
        Div[
            Button.Id("go-last").OnClick(SaveThenGo)["save"],
            Button.Id("go-then-await").OnClick(SaveGoAndCarryOn)["save and carry on"],
            Button.Id("go-then-read").OnClick(SaveGoAndRead)["save and read"]];

    private async Task SaveThenGo()
    {
        await ArrivalData.Save();
        Routes.ArrivalListPage().Go();
    }

    private async Task SaveGoAndCarryOn()
    {
        await ArrivalData.Save();
        Routes.ArrivalListPage().Go();
        await ArrivalData.Save();
    }

    // After Go() this page is gone and its lifetime with it: the read is cancelled, and the handler ends there.
    private async Task SaveGoAndRead()
    {
        await ArrivalData.Save();
        Routes.ArrivalListPage().Go();
        await ArrivalData.Save();
        await ArrivalData.Read();
    }
}

[Route("list")]
[ParentRoute(typeof(ArrivalLayout))]
public sealed partial class ArrivalListPage : Component
{
    private int? _rows;
    private int? _total;

    protected override async Task OnMount()
    {
        _rows = await ArrivalData.Read();
        _total = await ArrivalData.Read();
    }

    protected override Component? Render() =>
        Span.Id("list")[_total is null ? "loading" : $"rows:{_rows} total:{_total}"];
}

// A page that decides on load that the visitor belongs on the list.
[Route("moved")]
[ParentRoute(typeof(ArrivalLayout))]
public sealed partial class ArrivalMovedPage : Component
{
    protected override Task OnMount()
    {
        Routes.ArrivalListPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => Span.Id("moved")["moved"];
}
