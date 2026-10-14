# Rask.Print

**Print a page from the browser** — for a report, an invoice or a form you lay out as an ordinary Rask page.
No Chromium, no PDF library, nothing on the server: the browser's own print dialog does the work, and its
"Save as PDF" makes the PDF (the file is named after the page's title).

```csharp
using Rask.Printing;

Print.Styles(PrintPage.Size("A4").Landscape().Margins("15mm")),   // the @page rule
Print.Hide(Nav[...]),                                             // on screen only
Print.Only(P["Printed on " + today]),                             // on paper only
Button.Print()["Print this page"]                                 // opens the print dialog
```

- `Print.Only(...)` / `Print.Hide(...)` — show a part of the page on paper only, or everywhere but on paper.
- `Print.Styles(page)` — a `<style>` with those two rules and the page's `@page` rule; put it once in the page.
- `PrintPage.Size("A4" | "Letter" | "210mm 297mm").Landscape().Margins("15mm")` — only paper names and CSS lengths are
  accepted, since the text lands in a stylesheet.
- `Button.Print()` — a click calls `window.print()` through `Rask.Web`.

Full guide: [rask.sh/docs/printing](https://rask.sh/docs/printing/). Part of [Rask](https://rask.sh).
