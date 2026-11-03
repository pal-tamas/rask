using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// A `.Confirm` yes presses its button again from the page the server said the browser holds (the caughtUp answer),
// and a board drawn every few dozen ms while a long sending reports its progress is ten pages further on by the time
// the press arrives — far past the two a slot remembers, and the delete a new closure in every one of them. The page
// the answer followed is kept whole, and the press finds through it the handler the reader said yes to: still only
// one the page has now that is the same as it.
public partial class StaleClickFromAKeptPageTests : global::Rask.Core.RaskMarkup
{
    private const int PagesSinceTheAnswer = 10;

    public enum Scene
    {
        Delete,
        Gone,
        Archive,
    }

    // A row as a board reads it anew on every change: a new object every render.
    private sealed record Well(int Read);

    [Fact]
    public async Task A_press_from_the_kept_page_reaches_the_delete_ten_pages_later()
    {
        var sheet = new Sheet();
        using var cache = new SessionRenderCache();
        var readFrom = Render(cache, sheet.Page, sent: true);
        sheet.Page.KeepPageForPress();
        RenderMore(cache, sheet);

        var ran = await sheet.Page.TryInvokeHandlerAsync("h0", Click(readFrom));

        Assert.True(ran);
        Assert.Equal((1, 0, 0), (sheet.Deleted, sheet.Archived, sheet.Refreshed));
    }

    [Fact]
    public async Task Without_the_kept_page_the_same_press_ten_pages_later_runs_nothing()
    {
        var sheet = new Sheet();
        using var cache = new SessionRenderCache();
        var readFrom = Render(cache, sheet.Page, sent: true);
        RenderMore(cache, sheet);

        var ran = await sheet.Page.TryInvokeHandlerAsync("h0", Click(readFrom));

        Assert.False(ran);
        Assert.Equal((0, 0, 0), (sheet.Deleted, sheet.Archived, sheet.Refreshed));
    }

    [Theory]
    [InlineData(Scene.Gone)]
    [InlineData(Scene.Archive)]
    public async Task A_press_from_the_kept_page_runs_nothing_when_the_delete_is_gone_or_another_action_stands_there(
        Scene meanwhile)
    {
        var sheet = new Sheet();
        using var cache = new SessionRenderCache();
        var readFrom = Render(cache, sheet.Page, sent: true);
        sheet.Page.KeepPageForPress();
        sheet.Now = meanwhile;
        RenderMore(cache, sheet);

        var ran = await sheet.Page.TryInvokeHandlerAsync("h0", Click(readFrom));

        Assert.False(ran);
        Assert.Equal((0, 0, 0), (sheet.Deleted, sheet.Archived, sheet.Refreshed));
    }

    [Fact]
    public async Task Once_the_browser_sends_from_a_later_page_the_kept_page_is_let_go()
    {
        var sheet = new Sheet();
        using var cache = new SessionRenderCache();
        var readFrom = Render(cache, sheet.Page, sent: true);
        sheet.Page.KeepPageForPress();
        RenderMore(cache, sheet);
        var later = Render(cache, sheet.Page, sent: true);
        await sheet.Page.TryInvokeHandlerAsync(sheet.Ticks % 2 == 1 ? "h0" : "h1", Click(later));

        var ran = await sheet.Page.TryInvokeHandlerAsync("h0", Click(readFrom));

        Assert.False(ran);
        Assert.Equal(0, sheet.Deleted);
    }

    private static void RenderMore(SessionRenderCache cache, Sheet sheet)
    {
        for (var i = 0; i < PagesSinceTheAnswer; i++)
        {
            sheet.Ticks++;
            Render(cache, sheet.Page, sent: true);
        }
    }

    private static JsonElement Click(int version) =>
        JsonDocument.Parse($"{{\"type\":\"click\",\"v\":{version}}}").RootElement;

    private static int Render(SessionRenderCache cache, Component page, bool sent)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([], out _, html);
        var version = page.HandlerVersionToSend(whole: true)!.Value;
        if (sent)
        {
            page.HandlersSent();
        }
        else
        {
            page.HandlersRepeated();
        }

        return version;
    }

    // The edit sheet: a control that comes and goes before the delete moves the delete's id, and the delete closes
    // over a row read anew in every render.
    private sealed class Sheet
    {
        internal Sheet() =>
            Page = new StubComponent(() =>
            {
                var well = new Well(Ticks);
                return Div[
                    Ticks % 2 == 1 ? Button.OnClick(() => Refreshed += well.Read)["Frissítés"] : null,
                    Now switch
                    {
                        Scene.Gone => null,
                        Scene.Archive => Button.Key("remove").OnClick(() => Archived += well.Read >= 0 ? 1 : 0)["Archiválás"],
                        _ => Button.Key("remove").OnClick(() => Deleted += well.Read >= 0 ? 1 : 0)["Törlés"],
                    }
                ];
            });

        internal Component Page { get; }

        internal int Ticks { get; set; }

        internal Scene Now { get; set; }

        internal int Deleted { get; private set; }

        internal int Archived { get; private set; }

        internal int Refreshed { get; private set; }
    }
}
