using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A callout that appears above a list and a note, in a real browser: the list does not load again and the
///     note keeps what was typed into it.
/// </summary>
/// <remarks>
///     A component was told apart by its place among ALL the children its owner writes, so the callout — one
///     child more, ahead of them — rebuilt both: the list's <c>OnMount</c> ran again and the note's field was
///     empty. The unit suites pin which instance is which; what only a browser shows is the page a person
///     was looking at staying as it was while the callout comes and goes.
/// </remarks>
public sealed class ConditionalSiblingJourneyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_callout_that_comes_and_goes_above_a_loaded_list_and_a_typed_note_leaves_both_as_they_were()
    {
        await using var session = await HookSession.OpenAsync<CalloutJourneyPage>(playwright);
        var page = session.Page;
        var list = page.Locator("#list");
        var echo = page.Locator("#echo");
        await Expect(list).ToContainTextAsync("3 rows");
        var loaded = await list.TextContentAsync();
        var loads = CalloutJourneyLoads.Count;
        await page.FillAsync("#note", "kept");
        await Expect(echo).ToHaveTextAsync("kept");

        await page.ClickAsync("#save");
        await Expect(page.Locator("#callout strong")).ToHaveTextAsync("Saved");
        var whileShown = (await list.TextContentAsync(), await echo.TextContentAsync(), await page.InputValueAsync("#note"));
        await page.ClickAsync("#dismiss");
        await Expect(page.Locator("#callout")).ToHaveCountAsync(0);
        // One more round trip, so a load the callout's going had started would have landed by now.
        await page.FillAsync("#note", "kept still");
        await Expect(echo).ToHaveTextAsync("kept still");

        Assert.Equal((loaded, "kept", "kept"), whileShown);
        await Expect(list).ToHaveTextAsync(loaded!);
        Assert.Equal(loads, CalloutJourneyLoads.Count);
    }
}

/// <summary>How many times the journey's list has loaded, in this process.</summary>
internal static class CalloutJourneyLoads
{
    private static int _count;

    public static int Count => Volatile.Read(ref _count);

    public static int Next() => Interlocked.Increment(ref _count);
}

/// <summary>The panel sits under the page rather than being it: a root is rendered on every frame regardless.</summary>
public sealed partial class CalloutJourneyPage : Component
{
    protected override Component? HeadAssets => Markup.Title["callout"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Main[CalloutJourneyPanel];
}

/// <summary>A callout written AHEAD of the list and the note, brought in by one button and dismissed by its own.</summary>
public sealed partial class CalloutJourneyPanel : Component
{
    private bool _saved;

    protected override Component? Render() =>
        Div[
            _saved
                ? Div.Id("callout")[Strong["Saved"], Button.Id("dismiss").OnClick(() => _saved = false)["dismiss"]]
                : null,
            Button.Id("save").OnClick(() => _saved = true)["save"],
            CalloutJourneyList,
            CalloutJourneyNote
        ];
}

/// <summary>Loads as it mounts, and says which load it is showing.</summary>
public sealed partial class CalloutJourneyList : Component
{
    private int? _load;
    private int _rows;

    protected override async Task OnMount()
    {
        await Task.Delay(20);
        _rows = 3;
        _load = CalloutJourneyLoads.Next();
    }

    protected override Component? Render() =>
        P.Id("list")[_load is { } load ? $"{_rows} rows, load {load}" : "loading"];
}

/// <summary>Holds what was typed in a field of its own, and shows it beside the input.</summary>
public sealed partial class CalloutJourneyNote : Component
{
    private string _text = "";

    protected override Component? Render() =>
        Label[Input.Value(_text).Id("note").OnInput(v => _text = v ?? ""), Span.Id("echo")[_text]];
}
