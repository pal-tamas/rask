using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Server.Tests.Infrastructure;

// A layout that shows its page's title twice — a crumb in its header, and the document <title> — and the
// pages that declare one each way a real page does: a constant, a route parameter, a record loaded before
// the first await, one loaded after it, and one that takes longer than the response is allowed to wait.
public sealed partial class PageTitleApp : Component
{
    protected override Component? Render() => Router;
}

// The same pages under an App that writes the <title> ITSELF, above the router — where every scaffolded
// App has its HeadAssets. It renders before the router has mounted anything.
public sealed partial class PageTitleShellApp(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } t ? $"{t} | Shell" : "Shell"];

    protected override Component? Render() => Router;
}

// Under no layout, so the App above is the only thing that writes a <title>.
[Route("/bare/{Id}")]
public sealed partial class PageTitleBarePage : Component
{
    [RouteParam] public int Id { get; set; }

    protected override string? PageTitle => $"Edit Record {Id}";

    protected override Component? Render() => H1[PageTitle];
}

[Route("/titled")]
public sealed partial class PageTitleLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } t ? $"{t} | App" : "App"];

    protected override Component? Render() =>
        Div[
            Nav.Id("crumbs")[A.Href("/titled/list")["Home"], route.Title is { } t ? Span.Id("crumb")[t] : null],
            Main[Outlet]
        ];
}

[Route("list")]
[ParentRoute(typeof(PageTitleLayout))]
public sealed partial class PageTitleListPage : Component
{
    protected override string? PageTitle => "Records";

    protected override Component? Render() => H1["All records"];
}

[Route("plain")]
[ParentRoute(typeof(PageTitleLayout))]
public sealed partial class PageTitlePlainPage : Component
{
    protected override Component? Render() => H1["No title here"];
}

[Route("edit/{Id}")]
[ParentRoute(typeof(PageTitleLayout))]
public sealed partial class PageTitleEditPage : Component
{
    private string? _name;

    [RouteParam] public int Id { get; set; }

    protected override string? PageTitle => _name is { } n ? $"Edit {n}" : null;

    protected override Task OnUpdated()
    {
        _name = $"Record {Id}";
        return Task.CompletedTask;
    }

    protected override Component? Render() => H1[PageTitle];
}

[Route("fetched/{Id}")]
[ParentRoute(typeof(PageTitleLayout))]
public sealed partial class PageTitleFetchedPage : Component
{
    private string? _name;

    [RouteParam] public int Id { get; set; }

    protected override string? PageTitle => _name is { } n ? $"Edit {n}" : null;

    protected override async Task OnUpdated()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(40), CancellationToken);
        _name = $"Record {Id}";
    }

    protected override Component? Render() => _name is null ? P["Loading"] : H1[PageTitle];
}

[Route("slow")]
[ParentRoute(typeof(PageTitleLayout))]
public sealed partial class PageTitleSlowPage : Component
{
    // Completed by the test once the response has gone out without it.
    public static TaskCompletionSource Loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string? _name;

    protected override string? PageTitle => _name;

    protected override async Task OnMount()
    {
        await Loaded.Task;
        _name = "Slow record";
    }

    protected override Component? Render() => _name is null ? P["Loading"] : H1[_name];
}
