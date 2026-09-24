using System.Net;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     What the flash emitter hands the panel's script: the switch, the newest commits' places while it is on, and the
///     setting the page remembered coming back in.
/// </summary>
public sealed class DevToolsFlashEmitterTests
{
    private static DevToolsCommit Commit(long sequence, params (string? At, string Type, DevToolsRenderReason Reason)[] renders) =>
        new(sequence, sequence, renders.Length,
            renders.Select((r, i) => new DevToolsRender(i, r.Type, null, r.Reason, 1, r.At)).ToArray());

    [Fact]
    public void The_attribute_carries_the_newest_commits_places_labelled_with_type_and_reason()
    {
        var commits = Enumerable.Range(1, DevToolsFlashEmitter.CommitsCarried + 2)
            .Select(i => Commit(i, ("1|0|" + i, "Row", DevToolsRenderReason.State), (null, "Frame", DevToolsRenderReason.Props)))
            .ToArray();

        using var json = JsonDocument.Parse(DevToolsFlashEmitter.Flashes(commits));
        var carried = json.RootElement.EnumerateArray().ToList();

        Assert.Equal(DevToolsFlashEmitter.CommitsCarried, carried.Count);
        Assert.Equal(3, carried[0][0].GetInt64());
        Assert.Equal(DevToolsFlashEmitter.CommitsCarried + 2, carried[^1][0].GetInt64());
        // A render with no place of its own is not a box.
        var box = Assert.Single(carried[^1][1].EnumerateArray());
        Assert.Equal("1|0|10", box[0].GetString());
        Assert.Equal("Row · state", box[1].GetString());
    }

    [Fact]
    public void Off_it_says_so_and_carries_no_places()
    {
        var feed = new DevToolsFeed();
#pragma warning disable RASK014 // rendered alone, the way the panel page would chain it
        var page = Page.Render(new DevToolsFlashEmitter { Feed = feed, On = false });
#pragma warning restore RASK014

        Assert.Equal("off", page.Find("[data-rask-devtools-flash]").Attributes["data-rask-devtools-flash"]);
        Assert.Empty(page.FindAll("[data-rask-devtools-flashes]"));
        Assert.False(feed.WantsPlaces);
    }

    [Fact]
    public void On_it_carries_the_places_and_asks_the_feed_to_record_them()
    {
        var feed = new DevToolsFeed();
        var ids = new DevToolsTreeSnapshotter();
#pragma warning disable RASK014 // a component made by hand, standing in for one a walk would report
        feed.RecordCommit([new DevToolsRenderItem(new DevToolsTestChild(), RenderCause.Props, 1)], 1, ids, 1);
        var page = Page.Render(new DevToolsFlashEmitter { Feed = feed, On = true });
#pragma warning restore RASK014

        Assert.Equal("on", page.Find("[data-rask-devtools-flash]").Attributes["data-rask-devtools-flash"]);
        var raw = WebUtility.HtmlDecode(page.Find("[data-rask-devtools-flashes]").Attributes["data-rask-devtools-flashes"]);
        // The commit was recorded before anyone flashed, so it has no place: carried, with nothing to box.
        Assert.Matches(@"^\[\[\d+,\[\]\]\]$", raw);
        Assert.True(feed.WantsPlaces);
    }

    [Theory]
    [InlineData("flash:on", true)]
    [InlineData("flash:off", false)]
    public async Task The_remembered_setting_comes_back_as_a_keydown(string key, bool expected)
    {
        bool? reported = null;
#pragma warning disable RASK014 // rendered alone, the way the panel page would chain it
        var page = Page.Render(new DevToolsFlashEmitter
        {
            Feed = new DevToolsFeed(),
            On = !expected,
            OnChange = new Callback<bool>(on => reported = on),
        });
#pragma warning restore RASK014

        await page.On("[data-rask-devtools-flash]").RaiseAsync("keydown", "{\"key\":\"" + key + "\"}");

        Assert.Equal(expected, reported);
    }

    [Fact]
    public async Task Any_other_key_changes_nothing()
    {
        bool? reported = null;
#pragma warning disable RASK014 // rendered alone, the way the panel page would chain it
        var page = Page.Render(new DevToolsFlashEmitter
        {
            Feed = new DevToolsFeed(),
            On = false,
            OnChange = new Callback<bool>(on => reported = on),
        });
#pragma warning restore RASK014

        await page.On("[data-rask-devtools-flash]").RaiseAsync("keydown", "{\"key\":\"flash:maybe\"}");
        await page.On("[data-rask-devtools-flash]").RaiseAsync("keydown", "{\"key\":\"Enter\"}");

        Assert.Null(reported);
    }
}
