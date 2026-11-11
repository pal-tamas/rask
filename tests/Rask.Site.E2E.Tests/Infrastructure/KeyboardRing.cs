using System.Text.Json;
using Microsoft.Playwright;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     THE KEYBOARD RING (owner, 2026-11-10), walked as a reader walks it: Tab through everything a region holds and, at
///     each stop, read what focus draws — the ring is in the app's accent, and none of it lies outside the nearest box
///     around it that clips what overflows (a table's scroll frame, a sidebar, a dialog, an open list).
/// </summary>
/// <remarks>
///     Shared by the site's sweep (WASM) and the Server host's (linked into Rask.Server.E2E.Tests), so both hosts are
///     held to one reading.
/// </remarks>
public static class KeyboardRing
{
    // Runs in the page at each stop: where focus is, and what is wrong with its ring (null when nothing is).
    private const string Read = """
        async (scope) => {
          // What the Tab started fading in (a switch's track eases every colour) is read once it has arrived.
          await Promise.all(document.getAnimations().filter(a => a instanceof CSSTransition).map(a => a.finished.catch(() => null)));
          const active = document.activeElement;
          const region = document.querySelector(scope);
          if (!active || active === document.body || !region || !region.contains(active)) return { done: true };
          if (active.localName === 'iframe') return { key: 'iframe' };
          const said = el => el.localName + (el.id ? '#' + el.id : '')
            + [...el.attributes].filter(a => a.name.startsWith('data-ui-') || a.name === 'role' || a.name === 'type').map(a => `[${a.name}]`).join('')
            + ` "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 24)}"`;
          const key = said(active);
          // The accent the control itself computes: an example may set its own for what is inside it, a coloured
          // primary button its own.
          const accentAt = el => {
            const probe = document.body.appendChild(document.createElement('i'));
            probe.style.color = getComputedStyle(el).getPropertyValue('--color-accent').trim();
            const colour = getComputedStyle(probe).color;
            probe.remove();
            return colour;
          };

          // What draws the ring: the focused element, or what is seen of a hidden one (a checkbox's box round its input).
          const drawn = el => { const b = el.getBoundingClientRect(); return b.width > 2 && b.height > 2; };
          const outlined = el => { const s = getComputedStyle(el); return s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0; };
          const shadowed = el => getComputedStyle(el).boxShadow.includes('inset');
          let bearer = null;
          for (let up = active, hops = 0; up && hops < 4; up = up.parentElement, hops++) {
            // A field's box that holds the typed input (a pillbox, a colour or date picker) rings itself: its border and
            // an inset ring, in the accent.
            const boxed = up !== active && shadowed(up) && getComputedStyle(up).borderTopColor === accentAt(up);
            if (drawn(up) && (outlined(up) || (up === active && shadowed(up)) || boxed)) { bearer = up; break; }
          }
          // A menu's row, a list's option, a calendar's day the arrows move between: lit, not ringed, as Flux draws them.
          const lit = active.matches('[role^="menuitem"], [role="option"], [role="gridcell"], [contenteditable="true"]')
            || active.closest('[role="menu"], [role="listbox"], [role="grid"]');
          if (!bearer) return lit ? { key } : { key, fault: 'no ring' };
          const style = getComputedStyle(bearer);
          const accent = accentAt(bearer);
          const outline = outlined(bearer);
          if (outline && style.outlineStyle === 'auto') return { key, fault: `the browser's ring (auto ${style.outlineColor})` };
          const invalid = bearer.matches('[data-invalid], :has([data-invalid])');
          const colour = outline ? style.outlineColor : style.borderTopColor;
          if (!invalid && colour !== accent) return { key, fault: `ring ${colour}, accent ${accent}` };

          // The ring's extent, and every box above it that clips what overflows, up to the top layer or a fixed box.
          const grow = outline ? Math.max(0, parseFloat(style.outlineWidth) + parseFloat(style.outlineOffset)) : 0;
          const box = bearer.getBoundingClientRect();
          const ring = { left: box.left - grow, right: box.right + grow, top: box.top - grow, bottom: box.bottom + grow };
          for (let up = bearer.parentElement; up && up !== document.documentElement; up = up.parentElement) {
            const s = getComputedStyle(up);
            const [x, y] = [s.overflowX !== 'visible', s.overflowY !== 'visible'];
            if (x || y) {
              const r = up.getBoundingClientRect();
              const left = r.left + up.clientLeft, top = r.top + up.clientTop;
              const clip = { left, top, right: left + up.clientWidth, bottom: top + up.clientHeight };
              const cut = (x && (ring.left < clip.left - 0.5 || ring.right > clip.right + 0.5))
                || (y && (ring.top < clip.top - 0.5 || ring.bottom > clip.bottom + 0.5));
              // Wholly out of a scroller's view is scrolled away, not cut.
              const seen = ring.right > clip.left && ring.left < clip.right && ring.bottom > clip.top && ring.top < clip.bottom;
              if (cut && seen) {
                return { key, fault: `ring ${JSON.stringify(ring)} cut by ${said(up)} ${JSON.stringify(clip)}` };
              }
            }
            if (s.position === 'fixed' || up.matches(':popover-open, dialog:modal')) break;
          }
          return { key };
        }
        """;

    /// <summary>
    ///     Tabs through everything inside <paramref name="scope" />, from its start, and returns each stop whose ring is
    ///     not the accent or is cut by a box around it — "where: what", one line each. Empty when every ring is right.
    /// </summary>
    /// <param name="page">The page, loaded.</param>
    /// <param name="scope">The region walked: <c>main</c>, or a test page's own root.</param>
    /// <param name="stops">At most this many Tabs.</param>
    /// <returns>The faults; and how many stops were walked.</returns>
    public static async Task<(List<string> Faults, int Stops)> SweepAsync(IPage page, string scope = "main", int stops = 80)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.Mouse.MoveAsync(1, 1);
        await page.EvaluateAsync(
            "scope => { const r = document.querySelector(scope); r.setAttribute('tabindex', '-1'); r.focus({ preventScroll: true }); r.removeAttribute('tabindex'); }",
            scope);
        var faults = new List<string>();
        var walked = 0;
        for (; walked < stops; walked++)
        {
            await page.Keyboard.PressAsync("Tab");
            var at = await page.EvaluateAsync<JsonElement>(Read, scope);
            if (at.TryGetProperty("done", out _))
            {
                break;
            }

            if (at.TryGetProperty("fault", out var fault))
            {
                faults.Add($"{at.GetProperty("key").GetString()}: {fault.GetString()}");
            }
        }

        return (faults, walked);
    }

    /// <summary>Turns the page dark the way Ui.AppearanceScript does: a <c>dark</c> class on <c>&lt;html&gt;</c>.</summary>
    /// <param name="page">The page.</param>
    /// <param name="dark">Dark, or light.</param>
    /// <returns>A task.</returns>
    public static Task SchemeAsync(IPage page, bool dark)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.EvaluateAsync("dark => document.documentElement.classList.toggle('dark', dark)", dark);
    }
}
