# Rask.Testing.Playwright

**Wait for a Rask page to settle in a Playwright test** — `await page.WaitForRask()`. The Rask answer to Livewire's
`waitForLivewire()` for Laravel Dusk.

```csharp
using Rask.Testing;

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
