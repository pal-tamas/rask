# Rask.Print — printing a page from the browser

`Rask.Print` is for the report, the invoice or the delivery note you lay out yourself, as an ordinary Rask page. It
adds no engine: the browser prints, and its print dialog offers **Save as PDF**. Nothing runs on the server, no
Chromium is installed, and the PDF is whatever the reader's browser draws. (A spreadsheet is a different file:
see [Excel export](xlsx.md).)

```bash
dotnet add package Rask.Print
```

## A printable page

```csharp
using Rask.Printing;

public sealed partial class InvoicePage : Component
{
    protected override Component? Render() =>
        Div[
            Print.Styles(PrintPage.Size("A4").Margins("15mm")),
            Print.Hide(Nav[A.Href("/invoices")["All invoices"]]),
            H1["Invoice 1042"],
            Table[/* the lines */],
            Print.Only(P["Printed from rask.sh on 14 October 2026"]),
            Button.Print()["Print or save as PDF"],
        ];
}
```

- **`Print.Styles(page)`** writes one `<style>`: `.rask-print-only` is hidden on screen and shown on paper,
  `.rask-print-hide` is hidden on paper, and the `@page` rule carries the paper. Put it once on a page.
- **`Print.Only(...)`** and **`Print.Hide(...)`** wrap their children in a `<div>` with those classes.
- **`PrintPage`** is the `@page` rule as steps: `Size("A4")` (or `"Letter"`, or `"210mm 297mm"`), `Landscape()` /
  `Portrait()`, `Margins("15mm")` (or `"20mm 15mm"`). Text that is not a paper name or a CSS length throws, because it lands
  in a stylesheet.
- **`Button.Print()`** is a click handler that calls `window.print()` (MDN's `Window.Print`, from `Rask.Web`), so it needs
  an interactive page — the same as any `OnClick`.

## The file name

A browser names the saved PDF after the page's `<title>`. Set it from the page (`Head`'s title), and make it the
name you want the file to have: `Invoice 1042`.

## Print-CSS tips

Everything a browser's print stylesheet can do works in the page's own CSS (a scoped `.css` beside the component
works too):

```css
@media print {
    body { font-size: 11pt; color: #000; background: #fff; }
    a[href]::after { content: " (" attr(href) ")"; }   /* show where a link goes */
    tr, img { break-inside: avoid; }                   /* never split a row or a picture */
    h2 { break-after: avoid; }
    .page-break { break-before: page; }
}
```

Browsers drop background colours when printing unless the element asks for them:
`print-color-adjust: exact` on the element that needs its background. Test with the browser's print preview — no
headless PDF engine is involved, so what the preview shows is what the PDF holds.
