using System.Text.Json;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// The reusable MultiSelect<TItem> example component: a custom dropdown bound to an ICollection (bound mode)
// or driven by Value + OnChange (controlled mode). These drive the live handlers directly (open, select,
// deselect, Esc, click-outside) and assert the bound collection / emitted selection / rendered chips. The
// full browser flow is covered in SharedSmokeTests (Multi-select branch).
public sealed class MultiSelectTests
{
    private static readonly string[] Options = ["a", "b", "c"];

    private static (LiveHost Host, Bag Model) MountBound()
    {
        var model = new Bag();
        var host = new LiveHost(
            () => Form(model)[MultiSelect<string>(() => model.Tags, Options)],
            TestServices.Default());
        return (host, model);
    }

    [Fact]
    public void Render_Closed_ShowsPlaceholderAndAllOptions()
    {
        var (host, _) = MountBound();
        var html = host.RenderAsLiveRoot();

        Assert.Contains("Select&#x2026;", html);           // default placeholder, HTML-encoded ellipsis
        Assert.DoesNotContain("dropdown-menu show", html);  // closed by default
        Assert.Equal(3, CountOccurrences(html, "dropdown-item"));
        Assert.DoesNotContain("badge", html);               // no chips when empty
    }

    [Fact]
    public async Task Toggle_OpensMenu_AndRendersBackdrop()
    {
        var (host, _) = MountBound();
        var ids = ClickIds(host.RenderAsLiveRoot());

        await host.TryInvokeHandlerAsync(ids[0], Empty()); // box toggle is the first click handler

        var html = host.RenderAsLiveRoot();
        Assert.Contains("dropdown-menu show", html);
        Assert.Contains("position-fixed", html);            // click-outside backdrop only when open
    }

    [Fact]
    public async Task ClickOutside_ClosesMenu()
    {
        var (host, _) = MountBound();
        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty()); // open

        var openIds = ClickIds(host.RenderAsLiveRoot()); // [box, opt-a, opt-b, opt-c, backdrop]
        await host.TryInvokeHandlerAsync(openIds[^1], Empty()); // backdrop is the last click handler

        Assert.DoesNotContain("dropdown-menu show", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Escape_ClosesMenu()
    {
        var (host, _) = MountBound();
        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty()); // open

        var keyId = HandlerIds(host.RenderAsLiveRoot(), "data-rask-on-keydown")[0];
        await host.TryInvokeHandlerAsync(keyId, Json("{\"key\":\"Escape\"}"));

        Assert.DoesNotContain("dropdown-menu show", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Escape_OtherKey_DoesNotClose()
    {
        var (host, _) = MountBound();
        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[0], Empty()); // open

        var keyId = HandlerIds(host.RenderAsLiveRoot(), "data-rask-on-keydown")[0];
        await host.TryInvokeHandlerAsync(keyId, Json("{\"key\":\"ArrowDown\"}"));

        Assert.Contains("dropdown-menu show", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task SelectOption_AddsToCollection_RendersChipAndCheck()
    {
        var (host, model) = MountBound();
        var ids = ClickIds(host.RenderAsLiveRoot()); // [toggle, opt-a, opt-b, opt-c]

        await host.TryInvokeHandlerAsync(ids[2], Empty()); // select "b"

        Assert.Equal(["b"], model.Tags);
        var html = host.RenderAsLiveRoot();
        Assert.Contains("badge", html);                      // chip rendered for the selection
        Assert.Equal(1, CountOccurrences(html, " checked")); // exactly one option checked
    }

    [Fact]
    public async Task RemoveChip_RemovesFromCollection()
    {
        var (host, model) = MountBound();
        model.Tags.Add("a");
        // Layout order: box toggle, then chip-remove buttons (inside the box), then option rows.
        var ids = ClickIds(host.RenderAsLiveRoot()); // [toggle, chip-remove-a, opt-a, opt-b, opt-c]

        await host.TryInvokeHandlerAsync(ids[1], Empty()); // remove the "a" chip

        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task SelectingTwice_TogglesOff()
    {
        var (host, model) = MountBound();
        var ids = ClickIds(host.RenderAsLiveRoot());

        await host.TryInvokeHandlerAsync(ids[1], Empty()); // select "a"
        Assert.Equal(["a"], model.Tags);

        var ids2 = ClickIds(host.RenderAsLiveRoot()); // now [toggle, chip-remove-a, opt-a, opt-b, opt-c]
        await host.TryInvokeHandlerAsync(ids2[2], Empty()); // click the "a" option row again
        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task Validate_ShowsMessage_BelowMinimum_AndClearsWhenSatisfied()
    {
        var model = new Bag();
        var host = new LiveHost(
            () => Form(model)[
                MultiSelect<string>(
                    () => model.Tags,
                    Options,
                    Validate: tags => tags.Count >= 2 ? Array.Empty<string>() : ["Pick at least two."])],
            TestServices.Default());

        var ids = ClickIds(host.RenderAsLiveRoot());
        await host.TryInvokeHandlerAsync(ids[1], Empty()); // select "a" (count 1 < 2)
        Assert.Contains("Pick at least two.", host.RenderAsLiveRoot());

        var ids2 = ClickIds(host.RenderAsLiveRoot());
        await host.TryInvokeHandlerAsync(ids2[3], Empty()); // select "b" (count 2)
        Assert.DoesNotContain("Pick at least two.", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task AfterBind_FiresWithMutatedCollection()
    {
        var model = new Bag();
        ICollection<string>? seen = null;
        var host = new LiveHost(
            () => Form(model)[MultiSelect<string>(() => model.Tags, Options, AfterBind: c => seen = c)],
            TestServices.Default());

        var ids = ClickIds(host.RenderAsLiveRoot());
        await host.TryInvokeHandlerAsync(ids[2], Empty()); // select "b"

        Assert.NotNull(seen);
        Assert.Equal(["b"], seen!);
        Assert.Same(model.Tags, seen); // bound mode mutates the model collection in place
    }

    [Fact]
    public async Task AfterBindAsync_Fires()
    {
        var model = new Bag();
        var fired = false;
        var host = new LiveHost(
            () => Form(model)[MultiSelect<string>(
                () => model.Tags, Options, AfterBindAsync: _ =>
                {
                    fired = true;
                    return Task.CompletedTask;
                })],
            TestServices.Default());

        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[1], Empty());

        Assert.True(fired);
    }

    [Fact]
    public async Task Controlled_OnChange_EmitsNewSelection_WithoutMutatingValue()
    {
        var value = new List<string> { "a" };
        ICollection<string>? emitted = null;
        var host = new LiveHost(
            () => MultiSelect<string>(Options, Value: value, OnChange: next => emitted = next),
            TestServices.Default());

        var ids = ClickIds(host.RenderAsLiveRoot()); // [box, chip-remove-a, opt-a, opt-b, opt-c]
        await host.TryInvokeHandlerAsync(ids[3], Empty()); // select "b"

        Assert.NotNull(emitted);
        Assert.Equal(["a", "b"], emitted!);
        Assert.Equal(["a"], value); // controlled mode never mutates the parent's Value in place
    }

    [Fact]
    public async Task Controlled_OnChangeAsync_Fires()
    {
        var value = new List<string>();
        ICollection<string>? emitted = null;
        var host = new LiveHost(
            () => MultiSelect<string>(Options, Value: value, OnChangeAsync: next =>
            {
                emitted = next;
                return Task.CompletedTask;
            }),
            TestServices.Default());

        await host.TryInvokeHandlerAsync(ClickIds(host.RenderAsLiveRoot())[1], Empty()); // select "a"

        Assert.Equal(["a"], emitted!);
    }

    [Fact]
    public void Controlled_NoValidationMessage_NoEditContext()
    {
        var host = new LiveHost(
            () => MultiSelect<string>(Options, Value: new List<string>(), OnChange: _ => { }),
            TestServices.Default());

        var html = host.RenderAsLiveRoot();
        Assert.Contains("dropdown-item", html);
        Assert.DoesNotContain("invalid-feedback", html); // no bound field → no ValidationMessage
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
    public void NeitherBindNorValue_Throws()
    {
        var host = new LiveHost(
            () => MultiSelect<string>(Options),
            TestServices.Default());

        Assert.Throws<InvalidOperationException>(() => host.RenderAsLiveRoot());
    }

    private static JsonElement Empty() => Json("{}");

    private static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static List<string> ClickIds(string html) => HandlerIds(html, "data-rask-on-click");

    private static List<string> HandlerIds(string html, string attr)
    {
        var ids = new List<string>();
        var marker = attr + "=\"";
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
