using System.Reflection;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

#pragma warning disable RASK014 // test renders the demo component directly as a root

namespace Rask.Example.Shared.Tests.Pages;

// The keyed-lists reorder demo (folded into the Composition guide from its former standalone page). It
// keys rows by a stable id so a reorder preserves the survivors' DOM state; the keys-off branch shows
// positional reconciliation instead.
public sealed class KeyedListsPageTests
{
    [Fact]
    public void Demo_RendersSeededRows_KeyedByDefault()
    {
        var html = new KeyedListsReorderDemo().RenderAsLiveRoot(TestServices.Default());

        Assert.Contains("Apple", html);
        Assert.Contains("Elderberry", html);
        Assert.Contains("kl-list", html);
        // Keyed by default — each row carries its stable identity.
        Assert.Contains("data-rask-key=\"1\"", html);
    }

    [Fact]
    public void KeysOn_ByDefault_EmitsDataRaskKeyPerRow()
    {
        var html = RenderRows(true);

        Assert.Contains("data-rask-key=\"1\"", html);
        Assert.Contains("data-rask-key=\"5\"", html);
        Assert.Contains("Apple", html);
    }

    [Fact]
    public void KeysOff_OmitsDataRaskKey_ButStillRendersRows()
    {
        var html = RenderRows(false);

        Assert.DoesNotContain("data-rask-key", html);
        Assert.Contains("Apple", html);
    }

    // Render just the list rows (which have no DI dependencies) by invoking the demo's private
    // BuildRows() — rendering the whole demo through CodeSample would require a live render context.
    private static string RenderRows(bool useKeys)
    {
        var demo = new KeyedListsReorderDemo();
        typeof(KeyedListsReorderDemo)
            .GetField("_useKeys", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(demo, useKeys);

        var rows = (List<Component>)typeof(KeyedListsReorderDemo)
            .GetMethod("BuildRows", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(demo, null)!;

        return string.Concat(rows.Select(r => r.ToHtml()));
    }
}
