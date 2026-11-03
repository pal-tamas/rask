using Microsoft.Playwright;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     One headless Chromium, installed and launched on the FIRST read of <see cref="Browser" /> or
///     <see cref="Playwright" /> rather than when the fixture is created.
/// </summary>
/// <remarks>
///     Lazy because Rask.Site.E2E.Tests holds this as an ASSEMBLY fixture, which xUnit creates before any test
///     in the assembly runs — including the unit gate's run of the convention tests linked into every test
///     project, which never drive a browser and should not download one. A test that does pays for the launch
///     on its first use, once for the whole assembly.
/// </remarks>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private readonly Lazy<(IPlaywright Playwright, IBrowser Browser)> _started;

    public PlaywrightFixture() =>
        // Task.Run keeps the launch off whatever synchronization context the first test's thread carries, so
        // blocking on it here cannot deadlock; ExecutionAndPublication makes the tests that race for it share
        // one launch (and one failure, if it fails).
        _started = new Lazy<(IPlaywright, IBrowser)>(
            () => Task.Run(StartAsync).GetAwaiter().GetResult(),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public IPlaywright Playwright => _started.Value.Playwright;

    public IBrowser Browser => _started.Value.Browser;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_started.IsValueCreated)
        {
            return;
        }

        var (playwright, browser) = _started.Value;
        await browser.CloseAsync();
        playwright.Dispose();
    }

    private static async Task<(IPlaywright, IBrowser)> StartAsync()
    {
        // Fully qualified: this file is source-linked into Rask.Templates.E2E.Tests too, where a
        // bare `Program` binds to that assembly's own generated entry point instead.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"playwright install chromium exited with code {exitCode}");
        }

        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        return (playwright, browser);
    }
}
