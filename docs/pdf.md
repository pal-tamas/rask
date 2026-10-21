# Rask.Pdf — a page or component to PDF

`Rask.Pdf` prints a page of your app — or a component written only for print — to PDF, exactly as the user who
asks for it sees it. The page is rendered on the server as its first GET would be, and drawn by a headless
Chromium beside the app: real fonts and every script, a table heading repeated on every page, a footer line and
page numbers.

```bash
dotnet add package Rask.Pdf
```

A `RaskApp` turns it on when the app references the package. A hand-wired host calls
`builder.Services.AddRaskPdf()`.

## Print a page

```csharp
using Rask.Printing;

// A button on the report page.
Ui.Button.OnClick(async () =>
    Download.File("visszaterites.pdf", await Pdf.From(Routes.RefundReport(id)), Pdf.ContentType))["Letöltés PDF-ben"]
```

`Pdf.From(route)` takes a route of the app (`Routes.X(...)`), never a URL from user input. It:

1. **Checks the page's `[Authorize]` for the caller** — the session's user in a handler or a render, the request's
   user in a minimal-API endpoint, the enqueuing user in a [job](jobs.md#signed-in-as-that-user-never-with-more-rights).
   A page they could not open is never printed: `UnauthorizedAccessException`.
2. **Renders it as its first GET does** — the same layouts, the same data, in a session of its own that is dropped
   afterwards. No live connection, no runtime script. A page that redirects, or answers 404 or 500, is not printed.
3. **Prints it** in a browser context of its own, with the app's stylesheet, the kit's sheet and the fonts served
   from the app's web root. Nothing else is reachable — another host, an API, a file the app does not serve — and
   no script on the page runs.

The same call in a minimal-API endpoint:

```csharp
app.MapGet("/refunds/{id}.pdf", async (Guid id) =>
    Results.File(await Pdf.From(Routes.RefundReport(id)), Pdf.ContentType, "visszaterites.pdf"));
```

## Or a component written for print

```csharp
var receipt = await Pdf.From(Receipt.Order(order));
```

The component is rendered as a whole document of the app: its shell, its stylesheet, its fonts. There is no route,
so no `[Authorize]` to run: the caller already holds the data it was given.

## Write the report with Flux pieces

A report is an ordinary component, and the kit already knows what paper is:

- **Controls are left off.** Every `Ui.Button`, a badge's ×, `Ui.Pagination`, `Ui.Sidebar` and its toggle,
  `Ui.Header`, toasts, tooltips, open dropdowns and popovers, and a modal's backdrop print as nothing, and
  `Ui.Main` drops its screen padding and panel.
- **`Ui.Table` prints whole.** Cells wrap instead of being cut, the scroll frame lets the table run on, its
  heading repeats at the top of every page, and a row is never split across two.

Hide your own parts with Tailwind's `print:hidden`; show something the kit hides with an important utility,
`print:inline-flex!`. None of this touches the screen: every rule is inside `@media print`, so the same
defaults apply when a reader prints the page from their browser.

<!-- demo:pdf-report -->

## Options

```csharp
var pdf = await Pdf.From(Routes.RefundReport(id), new PdfOptions
{
    Page = PdfPage.A4.Landscape,                 // PdfPage.A4 (upright) unless set
    Margins = PdfMargins.Millimetres(15),        // or Millimetres(vertical, horizontal); 10 mm unless set
    Footer = "Visszatérítés — 2026. III. negyedév",
    PageNumbers = true,                          // "1 / 5" at the foot, right; on unless switched off
    Tagged = false,                              // an accessible, tagged PDF; off unless asked for
});
```

- **`Footer`** is printed as text — markup in it is shown, never run — at the foot of every page, left.
- **`Tagged`** writes the document's structure (headings, tables, reading order) for screen readers. It is most of
  the file: a 209-page table comes out 12 MB tagged and 0.45 MB untagged, and prints in twice the time.

## The browser

Rask.Pdf prints with the **Chrome for Testing headless shell**, at one pinned version with a SHA-256 per platform
(`build/Rask.Pdf.ChromePin.props` in the package). It is never downloaded by the running app:

| Where | How the browser gets there |
| --- | --- |
| Your machine, `rask dev`, `dotnet test`, CI | Building an app that references Rask.Pdf fetches the pinned shell into the user cache — `~/Library/Caches/Rask`, `~/.cache/rask` (or `$XDG_CACHE_HOME/rask`), `%LOCALAPPDATA%\Rask\Cache` — once per version, after checking its SHA-256. |
| The container image | The scaffolded Dockerfile has `dotnet publish` fetch it into the build stage, copies it to `/opt/rask/chrome`, and installs the libraries it links against plus fonts — only when the app references Rask.Pdf. About 510 MB more on `aspnet:10.0`. |
| Anywhere else | Install Chromium or Google Chrome and set `Rask:Pdf:ChromiumPath`; the build's fetch is off with `<RaskPdfDownloadChromium>false</RaskPdfDownloadChromium>`. |

An app scaffolded before Rask.Pdf existed adds the same lines to its Dockerfile: copy them from any template's
(`src/Rask.Templates/server/Dockerfile`). The pin moves with Rask's daily dependency run, only ever to a Stable
release at least a week old.

Without a browser, the first print fails with what to do — never a download.

## Settings

`Rask:Pdf` in `appsettings.json` (or `Rask__Pdf__…` in the environment):

| Setting | Default | |
| --- | --- | --- |
| `ChromiumPath` | the pinned shell, then the usual install folders and `PATH` | A browser of your own. |
| `NoSandbox` | `false` | Starts Chromium with `--no-sandbox`. Only for a host whose kernel forbids the sandbox's user namespaces; the documents printed are always the app's own. |
| `MaxConcurrentPrints` | `2` | How many documents print at once; the rest wait. Memory is the bound: a 5,000-row table peaks its renderer at about 1.2 GB. |
| `Timeout` | `00:00:30` | How long one document may take; longer is a `TimeoutException`. |
| `MaxPages` | `1000` | The longest document that prints; a longer one fails rather than come out cut. |

## How it runs

One headless Chromium per app, started at the first print and kept: a browser that crashed is replaced at the next
print, and stopping the host closes it. Rask drives it over its own DevTools pipe (`--remote-debugging-pipe`) —
no Puppeteer, no Playwright, no NuGet dependency. A 5,000-row table (139 A4 landscape pages, heading on every page)
prints in under a second on Linux.

Linux and macOS for now.
