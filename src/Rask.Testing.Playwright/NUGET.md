# Rask.Testing.Playwright

**Browser tests for a Rask app.** Run the app as a process of its own, walk every page, and wait for a page to
settle — the Rask answer to Laravel Dusk.

## Walk every page

```csharp
using Rask.Testing;

[assembly: AssemblyFixture(typeof(RaskSite<Program>))]

public sealed class EveryPageTests(RaskSite<Program> site)
{
    public static TheoryData<string> Pages() => [.. RaskSite.Pages<Program>().Without("/logout")];

    [Theory, MemberData(nameof(Pages))]
    public Task Every_page_opens_whole(string path) => site.Walk(path).As(site.Admin);
}
```

- `RaskSite<Program>` starts the app's own build (the test project references the app) as a process of its own, on a
  free port, with `ASPNETCORE_ENVIRONMENT=Testing` and a SQLite database in a folder of its own; it waits for the
  health check (`/health`), shares one Chromium, and ends the app — and every process it started — when disposed.
- `await site.Walk("/orders")` fails on an answer that is not a success (not found, forbidden), a console or page
  error, a request to the app that failed or answered 400+, a live connection opened twice or closed, the error
  overlay, and a page wider than a phone — naming the widest element. `.As(name)` walks it signed in.
- `await site.SignedIn(site.Admin)` — signed in once through Rask.Auth (its API, or the `/login` and `/register` pages
  `rask new` writes; the first account, with the app's one-time token), every later page from its cookies;
  `site.SignIn = async (page, name) => …` for an app's own form. `await site.SignedOut()` is a visitor's page.
- `RaskSite.Pages<Program>()` lists every page the app's route table opens without a value; `.And("/orders/1")`,
  `.Without("/logout")`.
- `site.Watch(page)` collects the same problems on a page a test drives; `site.Log` and `site.Said("…")` read what the
  app wrote. `site.Environment`, `site.Health`, `site.Contexts` (viewport, locale) set the site up.

## Wait for a page to settle

```csharp
await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
await page.WaitForRask();

Assert.Equal("Saved", await page.Locator("#status").TextContentAsync());
```

It waits until the page has settled: every event sent is answered and its render is on the page, every navigation
has landed, the frames received are applied, what was typed into a live or debounced field has been sent, the
connection is open, and the app has booted. No `WaitForTimeoutAsync`, no "DOM quiet for 300 ms" — which never
settles on a page that pushes a render every second, and settles too early on a slow one.

- **Both hosts**: Rask.Server and Rask.Wasm keep `data-rask-busy` on `<html>` while the page is settling; this reads
  it. The mark goes on in the same task as the click or key that caused it, so the wait cannot look too early.
- **Live pages settle**: a render the server pushes is applied in the task it arrives in.
- **Not covered**: the app's own timers, and a push that has not reached the browser yet — that is a new busy period.
- `await page.WaitForRask(5.Seconds)` waits at most that long; the default is the page's own timeout.
  `frame.WaitForRask()` waits for a Rask app inside an iframe.

Full guide: [rask.sh/docs/testing](https://rask.sh/docs/testing/). Part of [Rask](https://rask.sh).
