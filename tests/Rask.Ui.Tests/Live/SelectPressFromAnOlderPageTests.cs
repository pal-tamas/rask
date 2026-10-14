using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A reader ahead of the line: the rows of an open list are pressed before the page has answered. Every
///     press is read from the page as loaded, and each one the page hears is a render of the list — so the last
///     press arrives several pages late, and still picks its row. (Opening the list and moving about it are the
///     browser's now, and send nothing to be late.)
/// </summary>
/// <remarks>
///     An event from an older page reaches the handler that is the same as the one it was sent to, among the two
///     a slot remembers (<c>Component.StaleEvents</c>). The list's handlers closed over what each render worked
///     out, so no two renders made the same one: the third render in flight forgot the first, and the press ran
///     nothing.
/// </remarks>
public sealed partial class SelectPressFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Pistols = ["Glock", "Beretta", "Walther"];

    [Fact]
    public async Task A_third_row_pressed_before_the_page_answered_the_first_two_presses_is_picked_too()
    {
        List<string> picked = [];
        var page = Listbox(() => picked, values => picked = [.. values]);
        using var cache = new SessionRenderCache();
        var presses = Ids(Render(cache, page), "click");

        await Hear(cache, page, presses[0], At(10));
        await Hear(cache, page, presses[1], At(40));
        var heard = await Hear(cache, page, presses[2], At(70));

        Assert.True(heard);
        Assert.Equal(["Glock", "Beretta", "Walther"], picked);
    }

    [Fact]
    public async Task A_row_pressed_before_the_list_was_answered_with_other_rows_picks_nothing()
    {
        string[] offered = Pistols;
        List<string> picked = [];
        var page = Listbox(() => picked, values => picked = [.. values], () => offered);
        using var cache = new SessionRenderCache();
        var presses = Ids(Render(cache, page), "click");
        offered = ["Walther", "Glock", "Beretta"];
        Render(cache, page);

        var heard = await Hear(cache, page, presses[0], At(10));

        Assert.False(heard);
        Assert.Empty(picked);
    }

    private static StubComponent Listbox(Func<List<string>> picked, Action<ICollection<string>> onChange, Func<string[]>? offered = null) =>
        new(() => Ui.Select.Values(picked()).OnChange(onChange).Listbox.Multiple().Placeholder("Choose…")[
            (offered?.Invoke() ?? Pistols).Select(pistol => Ui.SelectOption.Key(pistol)[pistol])
        ]);

    // Where the pointer was, and the page the event was read from: the one the browser loaded.
    private static string At(int y) => $"{{\"clientX\":10,\"clientY\":{y},\"v\":0}}";

    private static List<string> Ids(string html, string domEvent) =>
        [.. Regex.Matches(html, $"data-rask-on-{domEvent}=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups[1].Value)];

    // One event of the session: its handler, then the render it is answered with.
    private static async Task<bool> Hear(SessionRenderCache cache, Component page, string id, string payload)
    {
        using var frame = JsonDocument.Parse(payload);
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
