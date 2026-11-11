using Microsoft.Playwright;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     The browser declining to carry out what the runtime asks of an overlay — a dialog's <c>showModal()</c> or
///     <c>close()</c>, a popover's <c>showPopover()</c> or <c>hidePopover()</c> — the next few times, as it does
///     mid-transition, on a busy processor or in a tab out of view: the call returns and nothing changes.
/// </summary>
/// <remarks>
///     What a slow line and a slow machine do to a page by chance — the one change the runtime was handed is lost —
///     done on purpose, every run: the page must still end up showing what the server last said.
/// </remarks>
public static class BrowserRefuses
{
    /// <summary>Patches the four calls on <paramref name="page" />'s prototypes, refusing none until <see cref="NextAsync" />.</summary>
    public static Task InstallAsync(IPage page) => page.EvaluateAsync(
        """
        () => {
            if (window.__refuse) return;
            window.__refuse = {};
            const patch = (proto, name) => {
                const real = proto[name];
                proto[name] = function (...args) {
                    if ((window.__refuse[name] || 0) > 0) { window.__refuse[name]--; return; }
                    return real.apply(this, args);
                };
            };
            patch(HTMLDialogElement.prototype, "showModal");
            patch(HTMLDialogElement.prototype, "close");
            patch(HTMLElement.prototype, "showPopover");
            patch(HTMLElement.prototype, "hidePopover");
        }
        """);

    /// <summary>The next <paramref name="times" /> calls of <paramref name="call" /> do nothing.</summary>
    public static Task NextAsync(IPage page, string call, int times = 1) =>
        page.EvaluateAsync("([call, times]) => { window.__refuse[call] = times; }", new object[] { call, times });
}
