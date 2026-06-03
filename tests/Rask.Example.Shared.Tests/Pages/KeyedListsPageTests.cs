using System.Reflection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class KeyedListsPageTests
{
    [Fact]
    public void Route_KeyedLists_RendersSeededRows()
    {
        var routeState = new RouteState { Path = "/keyed-lists" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("Apple", html);
        Assert.Contains("Elderberry", html);
        Assert.Contains("kl-list", html);
        // Keyed by default — each row carries its stable identity.
        Assert.Contains("data-rask-key=\"1\"", html);
    }

    [Fact]
    public void KeysOn_ByDefault_EmitsDataRaskKeyPerRow()
    {
        var html = RenderRows(useKeys: true);

        Assert.Contains("data-rask-key=\"1\"", html);
        Assert.Contains("data-rask-key=\"5\"", html);
        Assert.Contains("Apple", html);
    }

    [Fact]
    public void KeysOff_OmitsDataRaskKey_ButStillRendersRows()
    {
        var html = RenderRows(useKeys: false);

        Assert.DoesNotContain("data-rask-key", html);
        Assert.Contains("Apple", html);
    }

    // Render just the list rows (which have no DI dependencies) by invoking the page's
    // private BuildRows() — rendering the whole page would touch CodeSample, a DI component
    // that requires a live render context.
    private static string RenderRows(bool useKeys)
    {
        var page = new KeyedListsPage();
        typeof(KeyedListsPage)
            .GetField("_useKeys", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(page, useKeys);

        var rows = (List<Child>)typeof(KeyedListsPage)
            .GetMethod("BuildRows", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, null)!;

        return string.Concat(rows.Select(r => r.Component.ToHtml()));
    }
}
