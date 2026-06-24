using System.Text.Json;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// The reusable MultiSelect<TItem> example component: a custom dropdown bound to an ICollection. These
// tests drive the live handlers directly (open, select, deselect) and assert the bound collection +
// rendered chips/checks. The full browser flow is covered in SharedSmokeTests (Multi-select branch).
public sealed class MultiSelectTests
{
    private static readonly string[] Options = ["a", "b", "c"];

    private static (LiveHost Host, Bag Model) Mount(Action? onChange = null)
    {
        var model = new Bag();
        var host = new LiveHost(
            () => Form(model)[MultiSelect<string>(() => model.Tags, Options, OnChange: onChange)],
            TestServices.Default());
        return (host, model);
    }

    [Fact]
    public void Render_Closed_ShowsPlaceholderAndAllOptions()
    {
        var (host, _) = Mount();
        var html = host.RenderAsLiveRoot();

        Assert.Contains("Select&#x2026;", html);          // default placeholder, HTML-encoded ellipsis
        Assert.DoesNotContain("dropdown-menu show", html); // closed by default
        Assert.Equal(3, CountOccurrences(html, "dropdown-item"));
        Assert.DoesNotContain("badge", html);              // no chips when empty
    }

    [Fact]
    public async Task Toggle_OpensMenu()
    {
        var (host, _) = Mount();
        var ids = ClickIds(host.RenderAsLiveRoot());

        await host.TryInvokeHandlerAsync(ids[0], Empty()); // box toggle is the first click handler

        Assert.Contains("dropdown-menu show", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task SelectOption_AddsToCollection_RendersChipAndCheck()
    {
        var (host, model) = Mount();
        var ids = ClickIds(host.RenderAsLiveRoot()); // [toggle, opt-a, opt-b, opt-c]

        await host.TryInvokeHandlerAsync(ids[2], Empty()); // select "b"

        Assert.Equal(["b"], model.Tags);
        var html = host.RenderAsLiveRoot();
        Assert.Contains("badge", html);             // chip rendered for the selection
        Assert.Equal(1, CountOccurrences(html, " checked")); // exactly one option checked
    }

    [Fact]
    public async Task RemoveChip_RemovesFromCollection()
    {
        var (host, model) = Mount();
        model.Tags.Add("a");
        // Layout order: box toggle, then chip-remove buttons (inside the box), then option rows.
        var ids = ClickIds(host.RenderAsLiveRoot()); // [toggle, chip-remove-a, opt-a, opt-b, opt-c]

        await host.TryInvokeHandlerAsync(ids[1], Empty()); // remove the "a" chip

        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task SelectingTwice_TogglesOff()
    {
        var (host, model) = Mount();
        var ids = ClickIds(host.RenderAsLiveRoot());

        await host.TryInvokeHandlerAsync(ids[1], Empty()); // select "a"
        Assert.Equal(["a"], model.Tags);

        var ids2 = ClickIds(host.RenderAsLiveRoot()); // now [toggle, chip-remove-a, opt-a, opt-b, opt-c]
        await host.TryInvokeHandlerAsync(ids2[2], Empty()); // click the "a" option row again
        Assert.Empty(model.Tags);
    }

    [Fact]
    public void Disabled_ToggleBoxHasNoClickHandler()
    {
        var model = new Bag();
        var host = new LiveHost(
            () => Form(model)[MultiSelect<string>(() => model.Tags, Options, Disabled: true)],
            TestServices.Default());

        var html = host.RenderAsLiveRoot();

        Assert.Contains("form-select", html);
        Assert.Contains("disabled", html);
        Assert.DoesNotContain("data-rask-on-click", html); // inert: no toggle or option handlers wired
    }

    [Fact]
    public void NullOptions_ThrowsArgumentNullException()
    {
        var model = new Bag();
        var host = new LiveHost(
            () => Form(model)[MultiSelect<string>(() => model.Tags, null!)],
            TestServices.Default());

        Assert.Throws<ArgumentNullException>(() => host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task OnChange_FiresAfterSelection()
    {
        var fired = 0;
        var (host, _) = Mount(onChange: () => fired++);
        var ids = ClickIds(host.RenderAsLiveRoot());

        await host.TryInvokeHandlerAsync(ids[1], Empty());

        Assert.Equal(1, fired);
    }

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static List<string> ClickIds(string html)
    {
        var ids = new List<string>();
        const string marker = "data-rask-on-click=\"";
        var i = 0;
        while ((i = html.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            i += marker.Length;
            var end = html.IndexOf('"', i);
            ids.Add(html[i..end]);
            i = end;
        }

        return ids;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }

    private sealed class Bag
    {
        public List<string> Tags { get; } = [];
    }
}
