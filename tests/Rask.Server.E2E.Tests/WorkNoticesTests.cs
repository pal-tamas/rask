using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Background;
using Rask.Core;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> on a Server page (#1293), at a desktop's width and a phone's: as the job's
///     row moves, the bar moves with it on the page; when the job ends the list says so and a screen reader hears it, and
///     once the flyout has been opened the bell's count is gone.
/// </summary>
public sealed class WorkNoticesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task The_bar_moves_with_the_row_and_the_bell_count_goes_once_the_ended_job_is_seen(int width, int height)
    {
        var board = new ScriptedWorkBoard();
        board.Set(new WorkReported(Id(7), "Import of prices.csv", "1,200 rows kept", 1200, 5000, WorkState.Running, DateTime.UtcNow));
        await using var session = await HookSession.OpenAsync<WorkNoticesPage>(
            playwright, new() { ViewportSize = new() { Width = width, Height = height } },
            services: services => services.AddRaskCqrs().AddRaskQuery().AddSingleton<IWorkBoard>(board));
        var page = session.Page;
        var bar = page.Locator("[data-ui-work-list-of] [role=progressbar]");
        await Expect(bar).ToHaveAttributeAsync("aria-valuenow", "1200");
        await Expect(page.Locator("[data-ui-work-count]")).ToHaveTextAsync("1");

        board.Set(new WorkReported(Id(7), "Import of prices.csv", "3,000 rows kept", 3000, 5000, WorkState.Running, DateTime.UtcNow));
        await Expect(bar).ToHaveAttributeAsync("aria-valuenow", "3000");
        board.Set(new WorkReported(Id(7), "Import of prices.csv", "5,000 rows kept", 5000, 5000, WorkState.Done, DateTime.UtcNow));
        await Expect(page.Locator("[data-ui-work-list-of] [data-ui-work=done]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-ui-work-list-of] [data-ui-work-said]")).ToHaveTextAsync("Import of prices.csv is done.");
        await Expect(page.Locator("[data-ui-work-count]")).ToHaveTextAsync("1");
        await page.Locator("[data-ui-work-bell]").ClickAsync();
        await Expect(page.Locator("[data-ui-work-flyout] [data-ui-work=done]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator("[data-ui-work-count]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-ui-work-bell]")).ToHaveAttributeAsync("aria-label", "Background work");
        Assert.Empty(session.PageErrors);
    }

    /// <summary>The rows a test hands the page: the job processor's board, with no jobs behind it.</summary>
    private sealed class ScriptedWorkBoard : IWorkBoard
    {
        private volatile IReadOnlyList<WorkReported> _work = [];

        public TimeSpan Every => TimeSpan.FromMilliseconds(200);

        public void Set(params WorkReported[] work) => _work = work;

        public Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken) => Task.FromResult(_work);
    }

    // A job's id as a test names it: the nth, a version-7 Guid that reads as its number.
    private static Guid Id(int n) => new($"019ac4f0-5a00-7000-8000-{n:D12}");
}

/// <summary>An app's header with the bell, and the same work listed in the page.</summary>
public sealed partial class WorkNoticesPage : Component
{
    protected override Component? HeadAssets =>
        [Markup.Title["background work"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() =>
        Div.Attributes(("data-rask-ui-document", ""))[
            Ui.Header[Ui.Spacer, Ui.WorkNotices],
            Ui.Main[Ui.WorkList]
        ];
}
