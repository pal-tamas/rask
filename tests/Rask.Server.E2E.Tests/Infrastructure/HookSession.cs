using System.Collections.Concurrent;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     One page of plain markup, served by a real Rask.Server app with the real <c>rask.js</c>, open in a browser.
/// </summary>
/// <remarks>
///     What the runtime-hook suites share. The hooks are keyed on attributes, so the page under test is the
///     attributes written out by hand: going through a kit component would prove the kit as well, which only
///     <c>UiModalHookTests</c> sets out to do.
///     <para>
///         An error the page did not catch fails the journey that raised it, whatever that journey was looking at:
///         closing the session throws with every one of them. A runtime that throws in a listener usually breaks
///         nothing a test would see, which is how one went unnoticed.
///     </para>
/// </remarks>
internal sealed class HookSession : IAsyncDisposable
{
    private readonly LiveServerHost _host;
    private readonly IBrowserContext _context;
    private readonly ConcurrentQueue<string> _pageErrors;

    private HookSession(LiveServerHost host, IBrowserContext context, IPage page, ConcurrentQueue<string> pageErrors)
    {
        _host = host;
        _context = context;
        _pageErrors = pageErrors;
        Page = page;
    }

    /// <summary>Every error the page has raised and not caught, oldest first.</summary>
    public IReadOnlyCollection<string> PageErrors => _pageErrors;

    public IPage Page { get; }

    public string BaseUrl => _host.BaseUrl;

    /// <param name="playwright">The browser.</param>
    /// <param name="options">The context's options; a 1000 × 700 viewport when null.</param>
    /// <param name="beforeLoad">What to do to the page before it loads anything: an init script, a route.</param>
    /// <param name="path">The path the browser opens.</param>
    public static async Task<HookSession> OpenAsync<TPage>(
        PlaywrightFixture playwright, BrowserNewContextOptions? options = null, Func<IPage, Task>? beforeLoad = null,
        string path = "/")
        where TPage : Component
    {
        var host = await LiveServerHost.StartAsync<TPage>(blockWebSockets: false);
        var context = await playwright.Browser.NewContextAsync(options ?? new() { ViewportSize = new() { Width = 1000, Height = 700 } });
        var page = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);
        if (beforeLoad is not null)
        {
            await beforeLoad(page);
        }

        await page.GotoAsync(host.BaseUrl + path);
        return new HookSession(host, context, page, pageErrors);
    }

    /// <summary>How many times this page has asked the network for the behaviour hooks' bundle.</summary>
    public Task<int> HookBundleRequestsAsync() =>
        Page.EvaluateAsync<int>("() => performance.getEntriesByType('resource').filter(e => e.name.includes('/rask/rask-hooks.js')).length");

    /// <summary>Resolves once the behaviour hooks' bundle has run on this page.</summary>
    public Task HooksLoadedAsync() =>
        Page.WaitForFunctionAsync("() => performance.getEntriesByType('resource').some(e => e.name.includes('/rask/rask-hooks.js') && e.responseEnd > 0) && !window.__raskHookSeam.missed");

    /// <summary>Whether the element is a popover that is showing.</summary>
    public Task<bool> ShownAsync(string selector) =>
        Page.EvaluateAsync<bool>("s => document.querySelector(s).matches(':popover-open')", selector);

    /// <summary>The id of the element that has focus, or its tag name when it has none.</summary>
    public Task<string> FocusAsync() =>
        Page.EvaluateAsync<string>("() => document.activeElement.id || document.activeElement.tagName");

    /// <summary>Moves keyboard focus onto the element the way Tab does, so it is <c>:focus-visible</c>.</summary>
    public async Task TabToAsync(string selector)
    {
        await Page.EvaluateAsync("s => { const e = document.querySelector(s); const b = document.createElement('button'); b.id = '__before'; e.before(b); b.focus(); }", selector);
        await Page.Keyboard.PressAsync("Tab");
        await Page.EvaluateAsync("() => document.getElementById('__before').remove()");
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _host.DisposeAsync();
        Assert.True(
            _pageErrors.IsEmpty,
            "The page raised an error nothing caught:" + Environment.NewLine + string.Join(Environment.NewLine, _pageErrors));
    }
}
