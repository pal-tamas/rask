using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A thumb dragged faster than the page answers: every move is read from the page as loaded, and each one the
///     page hears is a render of the slider — so the last move arrives several pages late, and is still heard.
/// </summary>
/// <remarks>
///     An event from an older page reaches the handler that is the same as the one it was sent to, among the two
///     a slot remembers. The thumb's handler closed over what each render worked out, so no two renders made the
///     same one: the third render in flight forgot the first, and the move ran nothing.
/// </remarks>
public sealed partial class SliderMoveFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_move_sent_before_the_page_answered_three_earlier_ones_still_moves_the_value()
    {
        var volume = 30;
        var page = new StubComponent(() => Ui.Slider.Value(volume).OnChange(to => volume = to));
        using var cache = new SessionRenderCache();
        var moved = Regex.Match(Render(cache, page), "data-rask-on-input=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;

        await Hear(cache, page, moved, 40);
        await Hear(cache, page, moved, 50);
        await Hear(cache, page, moved, 60);
        var heard = await Hear(cache, page, moved, 70);

        Assert.True(heard);
        Assert.Equal(70, volume);
    }

    // One move of the session, read from the page the browser loaded: its handler, then the render that answers it.
    private static async Task<bool> Hear(SessionRenderCache cache, Component page, string id, int to)
    {
        using var frame = JsonDocument.Parse($"{{\"value\":\"{to}\",\"v\":0}}");
        var heard = await page.TryInvokeHandlerAsync(id, frame.RootElement);
        Render(cache, page);

        return heard;
    }

    // Renders, and says the browser was sent the result.
    private static string Render(SessionRenderCache cache, Component page)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([], out _, html);
        page.HandlersSent();

        return html;
    }
}
