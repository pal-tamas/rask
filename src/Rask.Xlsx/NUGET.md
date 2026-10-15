# Rask.Xlsx

**An Excel workbook from a list of rows** — `Xlsx.Write(sheet, headings, rows)` returns the `.xlsx` file's bytes.
Nothing but `System.IO.Compression` and hand-written OOXML: zero dependencies, on the server and in the browser.

- **Headings** in bold, frozen while the rows scroll, each with Excel's sort-and-filter button.
- **Columns** as wide as the longest thing in them (to a limit).
- **Amounts** (`decimal`) show two to four decimals with their thousands apart; `Xlsx.Day` and `Xlsx.Moment` show
  `yyyy.mm.dd` and `yyyy.mm.dd hh:mm`. A raw `DateTime`, `DateOnly` or `DateTimeOffset` is written as the same.
- **Folded rows** (`Xlsx.Folded`) group under the row before them, with Excel's plus and minus.
- **A total row** with `Xlsx.Sum`: a `SUBTOTAL` formula that follows whatever a filter leaves.
- **Safe by construction**: a text is always a string, never a formula — `=cmd|…` in a cell is shown, not run.

```csharp
using Rask.Workbooks;

byte[] file = Xlsx.Write(
    "Invoices",
    ["Number", "Day", "Amount"],
    invoices.Select(i => (IReadOnlyList<object?>)[i.Number, new Xlsx.Day(i.Issued), i.Amount]),
    total: [new Xlsx.Strong("Total"), null, new Xlsx.Sum()]);

return Results.File(file, Xlsx.ContentType, "invoices.xlsx");
```

Several sheets: `Xlsx.Write(new Xlsx.Sheet("Paid", headings, paid), new Xlsx.Sheet("Open", headings, open))`.
A sheet name Excel would reject (over 31 characters, `[ ] : * ? / \`) throws before anything is written.

Full guide: [rask.sh/docs/xlsx](https://rask.sh/docs/xlsx/). Part of [Rask](https://rask.sh).
