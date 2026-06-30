using System.Text.Json;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// BsRadioGroup<TValue> / BsCheckboxGroup<TItem> are example form controls (moved out of Rask.Core into the
// samples). These drive the live change handlers directly and assert the bound model + rendered checks.
public class RadioCheckboxGroupTests
{
    [Fact]
    public void RadioGroup_RendersOnePerOption_CurrentChecked()
    {
        var m = new ColorModel { Choice = Color.Green };
        var host = new LiveHost(
            () => Form(m)[BsRadioGroup(() => m.Choice, new[] { Color.Red, Color.Green, Color.Blue })],
            TestServices.Default());

        var html = host.RenderAsLiveRoot();

        Assert.Equal(3, CountOccurrences(html, "type=\"radio\""));
        Assert.Equal(3, CountOccurrences(html, "name=\"Choice\""));
        Assert.Equal(1, CountOccurrences(html, " checked")); // exactly one selected
        // The checked one is Green: its value attribute precedes the single `checked`.
        var greenIdx = html.IndexOf("value=\"Green\"", StringComparison.Ordinal);
        var checkedIdx = html.IndexOf(" checked", StringComparison.Ordinal);
        var blueIdx = html.IndexOf("value=\"Blue\"", StringComparison.Ordinal);
        Assert.True(greenIdx < checkedIdx && checkedIdx < blueIdx);
    }

    [Fact]
    public async Task RadioGroup_Change_SetsBoundValue()
    {
        var m = new ColorModel { Choice = Color.Red };
        var host = new LiveHost(
            () => Form(m)[BsRadioGroup(() => m.Choice, new[] { Color.Red, Color.Green, Color.Blue })],
            TestServices.Default());

        var html = host.RenderAsLiveRoot();
        var ids = AllChangeIds(html);
        Assert.Equal(3, ids.Count);

        // Fire the third radio (Blue). A radio change carries the checked state; the handler
        // ignores it and sets the bound value to its captured option.
        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        await host.TryInvokeHandlerAsync(ids[2], doc.RootElement);

        Assert.Equal(Color.Blue, m.Choice);

        // Re-render: the checked radio moved to Blue.
        var html2 = host.RenderAsLiveRoot();
        var blueIdx = html2.IndexOf("value=\"Blue\"", StringComparison.Ordinal);
        var checkedIdx = html2.IndexOf(" checked", StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html2, " checked"));
        Assert.True(blueIdx < checkedIdx);
    }

    [Fact]
    public void CheckboxGroup_RendersChecked_ForItemsInCollection()
    {
        var m = new TagsModel();
        m.Tags.Add("b");
        var host = new LiveHost(
            () => Form(m)[BsCheckboxGroup<string>(() => m.Tags, new[] { "a", "b", "c" })],
            TestServices.Default());

        var html = host.RenderAsLiveRoot();

        Assert.Equal(3, CountOccurrences(html, "type=\"checkbox\""));
        Assert.Equal(1, CountOccurrences(html, " checked")); // only "b"
        var bIdx = html.IndexOf("value=\"b\"", StringComparison.Ordinal);
        var checkedIdx = html.IndexOf(" checked", StringComparison.Ordinal);
        var cIdx = html.IndexOf("value=\"c\"", StringComparison.Ordinal);
        Assert.True(bIdx < checkedIdx && checkedIdx < cIdx);
    }

    [Fact]
    public async Task CheckboxGroup_Check_AddsToCollection()
    {
        var m = new TagsModel();
        var host = new LiveHost(
            () => Form(m)[BsCheckboxGroup<string>(() => m.Tags, new[] { "a", "b", "c" })],
            TestServices.Default());

        var html = host.RenderAsLiveRoot();
        var ids = AllChangeIds(html);

        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        await host.TryInvokeHandlerAsync(ids[0], doc.RootElement); // check "a"

        Assert.Contains("a", m.Tags);
        Assert.Single(m.Tags);
    }

    [Fact]
    public async Task CheckboxGroup_Uncheck_RemovesFromCollection()
    {
        var m = new TagsModel();
        m.Tags.Add("a");
        m.Tags.Add("b");
        var host = new LiveHost(
            () => Form(m)[BsCheckboxGroup<string>(() => m.Tags, new[] { "a", "b", "c" })],
            TestServices.Default());

        var html = host.RenderAsLiveRoot();
        var ids = AllChangeIds(html);

        using var doc = JsonDocument.Parse("{\"value\":\"false\"}");
        await host.TryInvokeHandlerAsync(ids[1], doc.RootElement); // uncheck "b"

        Assert.DoesNotContain("b", m.Tags);
        Assert.Contains("a", m.Tags);
    }

    [Fact]
    public async Task CheckboxGroup_Controlled_EmitsNewSelection_WithoutMutatingValue()
    {
        var value = new List<string> { "a" };
        ICollection<string>? emitted = null;
        var host = new LiveHost(
            () => BsCheckboxGroup<string>(new[] { "a", "b", "c" }, Value: value, OnChange: next => emitted = next),
            TestServices.Default());

        var ids = AllChangeIds(host.RenderAsLiveRoot());
        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        await host.TryInvokeHandlerAsync(ids[1], doc.RootElement); // check "b"

        Assert.Equal(["a", "b"], emitted!);
        Assert.Equal(["a"], value); // controlled mode never mutates the parent's Value
    }

    [Fact]
    public async Task RadioGroup_Controlled_EmitsSelectedValue()
    {
        Color? picked = null;
        var host = new LiveHost(
            () => BsRadioGroup(new[] { Color.Red, Color.Green, Color.Blue }, Value: Color.Red, OnChange: v => picked = v),
            TestServices.Default());

        var ids = AllChangeIds(host.RenderAsLiveRoot());
        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        await host.TryInvokeHandlerAsync(ids[2], doc.RootElement); // select Blue (third radio)

        Assert.Equal(Color.Blue, picked);
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

    private static List<string> AllChangeIds(string html)
    {
        var ids = new List<string>();
        const string marker = "data-rask-on-change=\"";
        var i = 0;
        while ((i = html.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            var start = i + marker.Length;
            var end = html.IndexOf('"', start);
            ids.Add(html.Substring(start, end - start));
            i = end;
        }

        return ids;
    }

    private enum Color
    {
        Red,
        Green,
        Blue
    }

    private sealed class ColorModel
    {
        public Color Choice { get; set; } = Color.Red;
    }

    private sealed class TagsModel
    {
        public List<string> Tags { get; } = new();
    }
}
