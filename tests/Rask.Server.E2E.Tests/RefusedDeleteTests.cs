using Rask.Core;
using Rask.Cqrs;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wire;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A list's bin whose delete the store refuses, in a real browser on a live server page that wrote nothing
///     for it: the reader is told why in a toast, the row is still there, and the page goes on working.
/// </summary>
public sealed class RefusedDeleteTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_delete_the_store_refuses_says_why_in_a_toast_keeps_the_row_and_leaves_the_page_usable()
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<TrailerListPage>(
            playwright,
            options: new() { ViewportSize = new() { Width = 1000, Height = 700 } },
            beforeLoad: page =>
            {
                frames = SocketFrames.Of(page);
                return Task.CompletedTask;
            },
            environment: "Production");
        var page = session.Page;
        await Expect(page.Locator("li")).ToHaveCountAsync(2);
        await page.ClickAsync("#bin-ABC-123");
        await Expect(page.Locator("dialog")).ToBeVisibleAsync();

        await page.ClickAsync("#confirm");

        await Expect(page.GetByText(TrailerListPage.Sentence)).ToBeVisibleAsync();
        await Expect(page.Locator("[data-ui-toast-dialog]")).ToHaveAttributeAsync("data-variant", "danger");
        await Expect(page.Locator("#row-ABC-123")).ToBeVisibleAsync();
        await Expect(page.Locator(".rask-error-boundary")).ToHaveCountAsync(0);
        await page.ClickAsync("#cancel");
        await Expect(page.Locator("dialog")).ToBeHiddenAsync();
        await page.ClickAsync("#bin-XYZ-789");
        await page.ClickAsync("#confirm");
        await Expect(page.Locator("li")).ToHaveCountAsync(1);
        await Expect(page.Locator("#row-ABC-123")).ToBeVisibleAsync();
        Assert.True(frames!.FullPages.Count == 0, "The refusal was answered with the whole document:" + Environment.NewLine + frames.Trace());
    }
}

/// <summary>
///     A list as an app writes it — a bin on each row, a confirm dialog, the kit's toasts in the layout and no
///     error handling of its own — where one row's delete is refused by the store.
/// </summary>
public sealed partial class TrailerListPage : Component
{
    public const string Sentence = "A pótkocsi nem törölhető, mert menetlevél hivatkozik rá.";

    private readonly List<string> _plates = ["ABC-123", "XYZ-789"];
    private string? _asking;

    protected override Component? HeadAssets => Markup.Title["a delete the store refuses"];

    protected override Component? Render() => Div[Main[
        Ul[_plates.Select(plate => (Component)Li.Key(plate).Id($"row-{plate}")[
            Span[plate],
            Button.Id($"bin-{plate}").OnClick(() => _asking = plate)["Törlés"]
        ])],
        Ui.Modal.Open(_asking is not null).OnClose(() => _asking = null)[
            Ui.Heading["Biztosan törli?"],
            Ui.Button.Id("cancel").OnClick(() => _asking = null)["Mégse"],
            Ui.Button.Danger.Id("confirm").OnClick(Delete)["Törlés"]
        ]
    ], Ui.Toast
    ];

    // As an app writes it: the store first, the dialog closed once it took the delete.
    private async Task Delete()
    {
        await Task.Delay(1);
        if (_asking == "ABC-123")
        {
            throw new RaskValidationException([new FieldFailure(Sentence, [], Source: "FK_Waybill_Trailer")]);
        }

        _plates.Remove(_asking!);
        _asking = null;
    }
}
