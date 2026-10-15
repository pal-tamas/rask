using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>.Confirm("…")</c> on the WebAssembly host, through the forms guide's demo: the browser's dialog is asked
///     before the click reaches .NET, and a refused one changes nothing. The Server host's half is
///     <c>ConfirmTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class ConfirmWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_dismissed_confirm_keeps_the_row_and_an_accepted_one_deletes_it() => RunAsync(async () =>
    {
        var answers = new Queue<string?>([null, ""]);
        var asked = Answer(answers);
        await OpenAsync();
        var delete = Page.Locator("#fc-delete-Acme");

        await delete.ClickAsync();
        await Expect(delete).ToBeVisibleAsync();
        await delete.ClickAsync();

        await Expect(Page.Locator("#fc-left")).ToHaveTextAsync("2 partners", new() { Timeout = 10_000 });
        await Expect(delete).ToHaveCountAsync(0);
        Assert.Equal(["confirm:Delete Acme?", "confirm:Delete Acme?"], asked);
    });

    [Fact]
    public Task The_prompt_deletes_only_when_the_name_is_typed_exactly() => RunAsync(async () =>
    {
        var answers = new Queue<string?>(["globex", "Globex"]);
        var asked = Answer(answers);
        await OpenAsync();
        var wipe = Page.Locator("#fc-wipe-Globex");

        await wipe.ClickAsync();
        await Expect(Page.Locator("#fc-left")).ToHaveTextAsync("3 partners");
        await wipe.ClickAsync();

        await Expect(Page.Locator("#fc-left")).ToHaveTextAsync("2 partners", new() { Timeout = 10_000 });
        Assert.Equal(["prompt:Type Globex to delete it", "prompt:Type Globex to delete it"], asked);
    });

    private List<string> Answer(Queue<string?> answers)
    {
        List<string> asked = [];
        Page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = answers.Dequeue() is { } text ? dialog.AcceptAsync(text) : dialog.DismissAsync();
        };
        return asked;
    }

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs + "/guides/forms");
        await WaitForInteractiveAsync();
        var demo = Page.Locator("#fc-demo");
        await Expect(demo).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await demo.ScrollIntoViewIfNeededAsync();
    }
}
