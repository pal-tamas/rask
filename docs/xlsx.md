# Rask.Xlsx — an Excel workbook from a list of rows

`Rask.Xlsx` writes an Excel workbook (`.xlsx`) from headings and rows and hands back its bytes. It uses nothing
but `System.IO.Compression` and hand-written OOXML — **zero dependencies**, and pure managed code, so it runs on a
server and in a WebAssembly app alike (`net10.0` and `net10.0-browser`).

```bash
dotnet add package Rask.Xlsx
```

## Write a sheet

```csharp
using Rask.Workbooks;

byte[] file = Xlsx.Write(
    "Invoices",
    ["Number", "Day", "Amount"],
    invoices.Select(i => (IReadOnlyList<object?>)[i.Number, new Xlsx.Day(i.Issued), i.Amount]),
    total: [new Xlsx.Strong("Total"), null, new Xlsx.Sum()]);

return Results.File(file, Xlsx.ContentType, "invoices.xlsx");
```

The call is `Xlsx.Write(string sheet, IReadOnlyList<string> headings, IEnumerable<IReadOnlyList<object?>> rows,
IReadOnlyList<object?>? total = null)`, or `Xlsx.Write(params Sheet[] sheets)` for a workbook of several:

```csharp
var file = Xlsx.Write(
    new Xlsx.Sheet("Paid", headings, paid),
    new Xlsx.Sheet("Open", headings, open));
```

## What a sheet looks like

- The **headings** are in bold and stay in place while the rows scroll; every column has Excel's sort-and-filter
  button (an `autoFilter` over the whole table).
- Every **column** is as wide as the longest thing in it, to 80 characters.
- A row is a list of cells; an empty cell (`null` or `""`) is left out, as Excel does.

## Cells

| You give | Excel shows |
|---|---|
| `string` | text (always a string: `=1+1` is shown, never worked out) |
| `int`, `long`, `double` and the other integers and floats | a number |
| `decimal` | an amount: two to four decimals, thousands apart (`1,250.50`) |
| `bool` | `TRUE` / `FALSE` |
| `Xlsx.Day(dt)` / `DateOnly` / a `DateTime` at midnight | `yyyy.mm.dd` |
| `Xlsx.Moment(dt)` / a `DateTime` with a time / `DateTimeOffset` | `yyyy.mm.dd hh:mm` |
| `Xlsx.Strong(value)` | the same, in bold |
| `Xlsx.Formula(text, now)` | a formula Excel works out itself, plus what it comes to now |
| `Xlsx.Sum` (in the `total` row) | `SUBTOTAL(9, …)` over the column |

A day or moment is the **clock on the wall**: a `DateTimeOffset` keeps its own local time, no zone is applied.
Anything else (a `Guid`, a `TimeSpan`, `NaN`) throws an `ArgumentException` that names the type, rather than
writing a file Excel would call corrupt — give such a value as text.

## Folded rows and totals

A row wrapped in `Xlsx.Folded` is hidden beneath the last row that is none (Excel's outline: a plus at the side,
a `2` at the corner for all of them):

```csharp
IReadOnlyList<object?>[] rows =
[
    ["Order 1042", 30m],
    new Xlsx.Folded(["  Widget", 10m]),
    new Xlsx.Folded(["  Gadget", 20m]),
];
```

The `total` row is the last row of the sheet. A `Sum` in it is a `SUBTOTAL(9, …)` formula over the amounts above, so
it follows whatever a filter leaves visible; Excel recomputes it as the file opens (`fullCalcOnLoad`).

## Sheet names

Excel refuses a name over 31 characters, one holding `[ ] : * ? / \`, one beginning or ending with an apostrophe,
an empty one, and two that differ only by case. `Xlsx.Write` throws an `ArgumentException` for each before it writes.

## Size and memory

The rows are enumerated once and held while the sheet is written (the column widths come first in the file and need
every row), and the sheet's XML is written straight into the archive entry, never built as one string. 100 000 rows of
three columns take well under a second. The result is a `byte[]`; for a very large export, write it to a file or the
response in pieces by sheet.

## Safety

A text is written as an inline string, so a cell starting with `=`, `+`, `-` or `@` is shown as text and is never a
formula (CSV-injection safe); only an explicit `Xlsx.Formula` is one. Control characters, which XML 1.0 cannot hold,
are written in Excel's own `_x0001_` escape.
