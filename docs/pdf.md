# Rask.Pdf — a page or component to PDF

`Rask.Pdf` prints a page of your app — or a component written only for print — to PDF, exactly as the user who
asks for it sees it. The page is rendered on the server as its first GET would be, and drawn by a headless
Chromium beside the app: real fonts and every script, a table heading repeated on every page, a footer line and
page numbers.

```bash
dotnet add package Rask.Pdf
```

Referencing the package turns it on, with no code of your own, on both kinds of host: a `RaskApp`, and a
hand-wired one (`builder.Services.AddRask()` + `app.MapRask<App>()`) — `AddRask()` finds Rask.Pdf beside it and
registers the printer. An explicit `builder.Services.AddRaskPdf()` is harmless (it registers once), and is only
needed on a host that does not call `AddRask()` at all.

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
   no script on the page runs. The page is laid out once, after it has loaded ([long documents](#long-documents)).

### A page's data is read before its first render is printed

The page prints from that first GET: there is **no socket**, so nothing arrives after it. Rask renders the page in
waves — render, wait for the async work that render started (every `OnMount` and `OnUpdated`, a query's first read),
render again — for up to `Rask:Server:QuiescenceTimeout` (5 s) in all, and prints the last wave. So a report that
awaits its data in `OnMount` prints the data, not its "Loading…":

```csharp
protected override async Task OnMount() => _rows = await Refunds.For(Quarter);
```

What that wait cannot cover is printed as it stands: work the page detaches (`_ = Poll()`), work waiting on the
browser (a JavaScript call never returns here), and anything still out when the budget is spent. A page that loads
its data some other way — a timer, a click — prints its placeholder. The budget is the server's for every first
GET; `Rask:Pdf:Timeout` is a separate limit on the print itself.

The page is rendered in the caller's language when the app ships it (`AddRaskCulture`), and in the app's default
otherwise.

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
- **`Ui.Accordion` prints open** — every item's content, closed ones included, with no chevron.

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
- **`PageNumbers`** are written from the `PdfPageNumber` catalog text (`{0}` the page, `{1}` the count) in the
  caller's language: `1 / 5` in English, `1 / 5 oldal` in Hungarian. Translate or reword it in your own
  `Resources/RaskStrings.{culture}.json` like any [framework text](localization.md).
- **`Tagged`** writes the document's structure (headings, tables, reading order) for screen readers. It is most of
  the file: a 209-page table comes out 12 MB tagged and 0.45 MB untagged, and prints in twice the time.

## The browser

Rask.Pdf prints with the **Chrome for Testing headless shell**, at one pinned version with a SHA-256 per platform
(`build/Rask.Pdf.ChromePin.props` in the package). It is never downloaded by the running app:

| Where | How the browser gets there |
| --- | --- |
| Your machine, `rask dev`, `dotnet test`, CI | Building an app that references Rask.Pdf fetches the pinned shell into the user cache — `~/Library/Caches/Rask`, `~/.cache/rask` (or `$XDG_CACHE_HOME/rask`), `%LOCALAPPDATA%\Rask\Cache` — once per version, after checking its SHA-256. |
| The container image | The scaffolded Dockerfile has `dotnet publish` fetch it into the build stage, copies it to `/opt/rask/chrome`, and installs the libraries it links against plus fonts — only when the app references Rask.Pdf. About 510 MB more on `aspnet:10.0`. It also sets `Rask__Pdf__NoSandbox=true` — see [In a container](#in-a-container). |
| Anywhere else | Install Chromium or Google Chrome and set `Rask:Pdf:ChromiumPath`; the build's fetch is off with `<RaskPdfDownloadChromium>false</RaskPdfDownloadChromium>`. |

An app scaffolded before Rask.Pdf existed adds the same lines to its Dockerfile: copy them from any template's
(`src/Rask.Templates/server/Dockerfile`). The pin moves with Rask's daily dependency run, only ever to a Stable
release at least a week old.

Without a browser, the first print fails with what to do — never a download.

### In a container

Chromium's sandbox needs unprivileged user namespaces, and a host on Ubuntu 24.04 or later refuses them to a
container (AppArmor), so the browser dies at start with "No usable sandbox". The scaffolded Dockerfile therefore sets
`ENV Rask__Pdf__NoSandbox=true`, as Browsershot, Grover and Puppeteer advise inside a container. What is given up is
small: the browser only ever prints the app's own pages, every request it makes is answered by Rask from the app's
origin, and scripts are off. On a host without that restriction you may delete the line; `rask dev` and `dotnet test`
keep the sandbox on.

A browser that will not start says why. A missing library is named with the Ubuntu package that ships it
(`libatk-bridge-2.0.so.0 is missing … apt-get install libatk-bridge2.0-0t64`), a refused sandbox with the setting
above — in the exception, and logged as an error under the `Rask.Pdf` category.

## Settings

`Rask:Pdf` in `appsettings.json` (or `Rask__Pdf__…` in the environment):

| Setting | Default | |
| --- | --- | --- |
| `ChromiumPath` | the pinned shell, then the usual install folders and `PATH` | A browser of your own. |
| `NoSandbox` | `false` (`true` in the scaffolded image) | Starts Chromium with `--no-sandbox`. For a container whose host forbids the sandbox's user namespaces; the documents printed are always the app's own. |
| `MaxConcurrentPrints` | `2` | How many documents print at once; the rest wait. Memory is the bound: a 5,000-row table peaks its renderer at about 1.2 GB. |
| `Timeout` | `00:00:30` | How long one document may take; longer is a `TimeoutException`. |
| `MaxPages` | `1000` | The longest document that prints; a longer one fails rather than come out cut. |

## How it runs

One headless Chromium per app, started at the first print and kept: a browser that crashed is replaced at the next
print, and stopping the host closes it. Rask drives it over its own DevTools pipe (`--remote-debugging-pipe`) —
no Puppeteer, no Playwright, no NuGet dependency. A 5,000-row table (139 A4 landscape pages, heading on every page)
prints in under a second on Linux.

### Long documents

The page is loaded **held out of rendering**, then styled and laid out once, in print media. A browser drawing a page
as it parses restyles the table grown so far at every frame — a cost that grows with the square of the rows — so the
hold is what keeps a long table linear: 10,000 kit table rows (14 columns, 250 pages) print in about 6 seconds on a
laptop, where they took 22. What is left grows with the rows: the HTML the page renders (every class on every cell
is sent and parsed once per cell) and Chromium paginating the table.

A table longer than ten pages is **cut into a table per page** before it prints, where Chromium breaks the pages
anyway: each piece keeps the table's column widths, its heading and footing, and the rows' `:first-child`/odd/even
places, so the PDF comes out with the same rows on the same pages. Chromium's print layout of one table pays for every
row on every page the table runs to — a plain 10,000-row table (455 pages) peaked its renderer at 10.6 GB, and a kit
`Ui.Table` on the kit's sheet (590 pages) passed 13 GB and failed with Chromium's bare "Printing failed". Cut, they
peak at about 3 and 4.5 GB, and the kit table prints in 9 seconds. A table with a `rowspan`, its own `<colgroup>`, or a
row taller than a page is left whole, as is every table of a page with an `@page` rule.

If Chromium still answers "Printing failed", `Pdf.From` throws an `InvalidOperationException` that says so and what
to do: print the document in parts (a PDF per month), split the table, or print it smaller so it takes fewer pages.
Streaming the PDF out of Chromium (`transferMode: ReturnAsStream`) or printing a page range does not help: the PDF
of 590 pages is under 3 MB, and Chromium lays the whole document out to print even one page of it.

A document in the hundreds of pages on a small server can still need more than the 30-second `Timeout`, and about
3 GB for its browser per 10,000 rows: raise `Timeout` in `Rask:Pdf` for that app (`"Timeout": "00:03:00"`), and keep
`MaxConcurrentPrints` within the memory the container has.

To measure it on your machine — a 14-column table at 1,000, 5,000 and 10,000 rows, plain, as a kit `Ui.Table`, on the
kit's sheet and with a 7pt print stylesheet, each with its HTML size, pages and where the time went — run the opt-in
measurement from the repository root (it is skipped without the variable, so CI never runs it):

```bash
RASK_PDF_MEASURE=1 dotnet test tests/Rask.Pdf.Tests --filter "FullyQualifiedName~PrintMeasurements" --logger "console;verbosity=detailed"
```

`RASK_PDF_MEASURE_OUT=/tmp/pdf-measure.md` also writes the table to that file.

Linux and macOS for now.
