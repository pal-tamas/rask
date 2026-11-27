using Microsoft.Playwright;
using Rask.Background;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wasm.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Wasm.E2E.Tests;

/// <summary>
///     <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> in the browser half of an app with a server (#1348), at a desktop's
///     width and a phone's: the rows come from the server for whoever is signed in there, and what the jobs publish moves
///     the bar and the bell's count over the watch the page opens on the server — never another user's work.
/// </summary>
/// <remarks>
///     The rows land just as the first frame waits for the kit's stylesheet, which is how it caught the render queue
///     running the next frame before the first had morphed the page — and the first then drawing over it.
/// </remarks>
public sealed class WorkNoticesTests(PlaywrightFixture playwright, ITestOutputHelper output) : IClassFixture<PlaywrightFixture>
{
    private static readonly Guid Ana = Guid.Parse("3c1f4e2a-9b7d-4c6e-8a5f-1d2b3c4d5e6f");
    private static readonly Guid Bo = Guid.Parse("9e8d7c6b-5a4f-4e3d-8c2b-1a0f9e8d7c6b");

    // The first WASM boot downloads and starts the runtime.
    private static readonly LocatorAssertionsToHaveAttributeOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    public static TheoryData<int, int> Viewports => new() { { 1280, 800 }, { 390, 844 } };

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task The_bar_moves_and_the_bell_counts_as_the_server_s_jobs_report_and_end(int width, int height)
    {
        var work = new WorkServer();
        work.Rows(
            Import(1200, WorkState.Running, Ana),
            new WorkReported(Id(5), "Bo's export", null, 1, 9, WorkState.Running, DateTime.UtcNow.AddMinutes(-1)) { Owner = Bo });
        await using var host = await SignInHost.StartAsync(work: work);
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        page.Console += (_, message) => output.WriteLine($"[console] {message.Text}");
        page.PageError += (_, error) => output.WriteLine($"[page error] {error}");
        await page.GotoAsync($"{host.BaseUrl}/token?user={Ana}");
        await page.GotoAsync(host.BaseUrl + "/work");
        var list = page.Locator("[data-ui-work-list-of]");
        var bar = list.Locator("[role=progressbar][aria-label='Import of prices.csv']");
        var count = page.Locator("[data-ui-work-count]");
        await Expect(bar).ToHaveAttributeAsync("aria-valuenow", "1200", Boot);
        await Expect(count).ToHaveTextAsync("1", Settled);
        await Expect(list).Not.ToContainTextAsync("Bo's export");

        await PublishUntil(host, Import(3000, WorkState.Running, Ana), () => bar.GetAttributeAsync("aria-valuenow"), "3000");
        await WorkServer.Publish(host, new WorkReported(Id(9), "Bo's import", null, 1, 2, WorkState.Running, DateTime.UtcNow) { Owner = Bo });
        await WorkServer.Publish(host, new WorkReported(Id(8), "Monthly fuel report", null, null, null, WorkState.Running, DateTime.UtcNow) { Owner = Ana });
        await Expect(count).ToHaveTextAsync("2", Settled);
        await Expect(list).Not.ToContainTextAsync("Bo's import");
        await WorkServer.Publish(host, Import(5000, WorkState.Done, Ana));
        await Expect(list.Locator("[data-ui-work=done]")).ToBeVisibleAsync();
        await Expect(list.Locator("[data-ui-work-said]")).ToHaveTextAsync("Import of prices.csv is done.", Settled);
        await Expect(count).ToHaveTextAsync("2", Settled);
        await page.Locator("[data-ui-work-bell]").ClickAsync();
        await Expect(page.Locator("[data-ui-work-flyout] [data-ui-work=done]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");

        await Expect(count).ToHaveTextAsync("1", Settled);
        await Expect(page.Locator("[data-ui-work-bell]")).ToHaveAttributeAsync("aria-label", "Background work, 1 new");
        await Expect(list).Not.ToContainTextAsync("Bo's");
    }

    private static WorkReported Import(int kept, WorkState state, Guid owner) =>
        new(Id(7), "Import of prices.csv", $"{kept:N0} rows kept", kept, 5000, state, DateTime.UtcNow) { Owner = owner };

    // The page opens its watch on the server after its first paint: what is published before that is not heard, so the
    // first report goes out again until the bar shows it — and from then on the watch is open.
    private static async Task PublishUntil(SignInHost host, WorkReported work, Func<Task<string?>> read, string shown)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await WorkServer.Publish(host, work with { At = DateTime.UtcNow });
            await Task.Delay(250);
            if (await read() == shown)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < until, $"the page never showed {shown}");
        }
    }

    // A job's id as a test names it: the nth, a version-7 Guid that reads as its number.
    private static Guid Id(int n) => new($"019ac4f0-5a00-7000-8000-{n:D12}");
}
