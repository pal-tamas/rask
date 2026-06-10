using System.Text.Json;

#pragma warning disable RASK014 // test uses the StubComponent test helper directly

namespace Rask.Core.Tests.Forms;

public class RadioCheckboxGroupTests
{
    [Fact]
    public void RadioGroup_RendersOnePerOption_CurrentChecked()
    {
        var m = new ColorModel { Choice = Color.Green };
        var view = new StubComponent(() =>
            Form(m)[RadioGroup(() => m.Choice, new[] { Color.Red, Color.Green, Color.Blue })]);

        var html = view.RenderAsLiveRoot();

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
        var view = new StubComponent(() =>
            Form(m)[RadioGroup(() => m.Choice, new[] { Color.Red, Color.Green, Color.Blue })]);

        var html = view.RenderAsLiveRoot();
        var ids = AllChangeIds(html);
        Assert.Equal(3, ids.Count);

        // Fire the third radio (Blue). A radio change carries the checked state; the handler
        // ignores it and sets the bound value to its captured option.
        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        await view.TryInvokeHandlerAsync(ids[2], doc.RootElement);

        Assert.Equal(Color.Blue, m.Choice);

        // Re-render: the checked radio moved to Blue.
        var html2 = view.RenderAsLiveRoot();
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
        var view = new StubComponent(() =>
            Form(m)[CheckboxGroup<string>(() => m.Tags, new[] { "a", "b", "c" })]);

        var html = view.RenderAsLiveRoot();

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
        var view = new StubComponent(() =>
            Form(m)[CheckboxGroup<string>(() => m.Tags, new[] { "a", "b", "c" })]);

        var html = view.RenderAsLiveRoot();
        var ids = AllChangeIds(html);

        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        await view.TryInvokeHandlerAsync(ids[0], doc.RootElement); // check "a"

        Assert.Contains("a", m.Tags);
        Assert.Single(m.Tags);
    }

    [Fact]
    public async Task CheckboxGroup_Uncheck_RemovesFromCollection()
    {
        var m = new TagsModel();
        m.Tags.Add("a");
        m.Tags.Add("b");
        var view = new StubComponent(() =>
            Form(m)[CheckboxGroup<string>(() => m.Tags, new[] { "a", "b", "c" })]);

        var html = view.RenderAsLiveRoot();
        var ids = AllChangeIds(html);

        using var doc = JsonDocument.Parse("{\"value\":\"false\"}");
        await view.TryInvokeHandlerAsync(ids[1], doc.RootElement); // uncheck "b"

        Assert.DoesNotContain("b", m.Tags);
        Assert.Contains("a", m.Tags);
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
