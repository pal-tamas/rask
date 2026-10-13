using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A reader ahead of the line at an autocomplete: the first key opened the list, and the keys after it, the
///     pointer onto an item and the press on it were all read from that one page. Each is a render of the list,
///     so the press arrives several pages late, and is still the text.
/// </summary>
/// <remarks>
///     The same rule <c>SelectPressFromAnOlderPageTests</c> holds the select to. The autocomplete's handlers
///     closed over what each render worked out, so a slot's handler was a new one in every render, the slot
///     remembered two, and a press read three pages back ran nothing.
/// </remarks>
public sealed partial class AutocompletePressFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Pistols = ["Glock", "Beretta", "Walther", "Bersa", "Colt"];

    [Fact]
    public async Task An_item_pressed_before_the_page_answered_the_keys_typed_after_the_first_is_the_text()
    {
        var text = string.Empty;
        var page = Field(() => text, said => text = said);
        using var cache = new SessionRenderCache();
        var typed = Ids(Render(cache, page).Html, "input")[0];
        await Hear(cache, page, typed, Typed("b", 0));
        var (opened, seen) = Render(cache, page);
        var (bersa, over, left) = (OnRow(opened, "Bersa", "click"), OnRow(opened, "Bersa", "mouseenter"), Ids(opened, "change")[0]);

        await Hear(cache, page, typed, Typed("be", seen));
        await Hear(cache, page, typed, Typed("ber", seen));
        await Hear(cache, page, over, At(seen));
        await Hear(cache, page, left, Typed("ber", seen));
        var heard = await Hear(cache, page, bersa, At(seen));

        Assert.True(heard);
        Assert.Equal("Bersa", text);
    }

    [Fact]
    public async Task An_item_pressed_before_the_list_was_answered_with_other_items_writes_nothing()
    {
        string[] offered = Pistols;
        var text = string.Empty;
        var page = Field(() => text, said => text = said, () => offered);
        using var cache = new SessionRenderCache();
        var typed = Ids(Render(cache, page).Html, "input")[0];
        await Hear(cache, page, typed, Typed("b", 0));
        var (opened, seen) = Render(cache, page);
        offered = ["Walther", "Colt", "Glock", "Bersa", "Beretta"];
        Render(cache, page);

        await Hear(cache, page, OnRow(opened, "Beretta", "click"), At(seen));

        Assert.Equal(string.Empty, text);
    }

    private static StubComponent Field(Func<string> text, Action<string> onChange, Func<string[]>? offered = null) =>
        new(() => Ui.Autocomplete.Value(text()).OnChange(onChange).Debounce(TimeSpan.FromMilliseconds(400))[
            (offered?.Invoke() ?? Pistols).Select(pistol => Ui.AutocompleteItem.Key(pistol)[pistol])
        ]);

    // What the field said at a key, and the page the event was read from.
    private static string Typed(string value, int page) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"value\":\"{value}\",\"v\":{page}}}");

    // Where the pointer was, and the page the event was read from.
    private static string At(int page) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"clientX\":10,\"clientY\":40,\"v\":{page}}}");

    private static List<string> Ids(string html, string domEvent) =>
        [.. Regex.Matches(html, $"data-rask-on-{domEvent}=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups[1].Value)];

    // The handler an item's row carries for one event.
    private static string OnRow(string html, string item, string domEvent)
    {
        var row = Regex.Match(html, $"<div[^>]*role=\"option\"[^>]*>(?={item}<)", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;

        return Ids(row, domEvent).Single();
    }

    // One event of the session: its handler, then the render it is answered with.
    private static async Task<bool> Hear(SessionRenderCache cache, Component page, string id, string payload)
    {
        using var frame = JsonDocument.Parse(payload);
        var heard = await page.TryInvokeHandlerAsync(id, frame.RootElement);
        Render(cache, page);

        return heard;
    }

    // Renders, and says the browser was sent the result: the markup, and the number that page carries.
    private static (string Html, int Page) Render(SessionRenderCache cache, Component page)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([], out _, html);
        var number = page.HandlerVersionToSend(whole: true)!.Value;
        page.HandlersSent();

        return (html, number);
    }
}
