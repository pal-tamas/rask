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
/// </remarks>
internal sealed class HookSession : IAsyncDisposable
{
    private readonly LiveServerHost _host;
    private readonly IBrowserContext _context;

    private HookSession(LiveServerHost host, IBrowserContext context, IPage page)
    {
        _host = host;
        _context = context;
        Page = page;
    }

    public IPage Page { get; }

    public string BaseUrl => _host.BaseUrl;

    public static async Task<HookSession> OpenAsync<TPage>(PlaywrightFixture playwright, BrowserNewContextOptions? options = null)
        where TPage : Component
    {
        var host = await LiveServerHost.StartAsync<TPage>(blockWebSockets: false);
        var context = await playwright.Browser.NewContextAsync(options ?? new() { ViewportSize = new() { Width = 1000, Height = 700 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        return new HookSession(host, context, page);
    }

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
    }
}
