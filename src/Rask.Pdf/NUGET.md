# Rask.Pdf

**A PDF of a Rask page or component** — `await Pdf.From(Routes.RefundReport(id))` returns the PDF's bytes, drawn
by a headless Chromium beside the app, exactly as the calling user sees the page.

- **The page as its first GET renders it**, on the server, for the caller — the session's user in a handler, the
  request's in an endpoint, the enqueuing user in a job. Its `[Authorize]` runs first: a page they could not open
  is never printed.
- **The app's own stylesheet and fonts**, served to the browser from the app's web root. Nothing else is reachable,
  and no script runs.
- **Every script, every accent** — the real fonts are embedded, so ő and ű come out right, copyable and searchable.
- **Tables that run over pages** repeat their heading on each; a foot line and "1 / 5" page numbers are one option.
- **The browser comes from the build, never from the running app**: building an app that references Rask.Pdf
  fetches the pinned Chrome for Testing headless shell into the user cache (its SHA-256 checked first), and the
  scaffolded Dockerfile puts it in the image. Zero NuGet dependencies — Rask drives the browser over its own
  DevTools pipe.

```csharp
using Rask.Printing;

var pdf = await Pdf.From(Routes.RefundReport(id), new PdfOptions
{
    Page = PdfPage.A4.Landscape,
    Footer = "Visszatérítés — 2026. III. negyedév",
});
Download.File("visszaterites.pdf", pdf, Pdf.ContentType);   // from a button's handler

app.MapGet("/refunds/{id}.pdf", async (Guid id) =>
    Results.File(await Pdf.From(Routes.RefundReport(id)), Pdf.ContentType));
```

A `RaskApp` turns it on when the app references the package; a hand-wired host calls
`builder.Services.AddRaskPdf()`. Settings live under `Rask:Pdf`: `ChromiumPath`, `NoSandbox`,
`MaxConcurrentPrints`, `Timeout`, `MaxPages`. Linux and macOS.

Full guide: [rask.sh/docs/pdf](https://rask.sh/docs/pdf/). Part of [Rask](https://rask.sh).
