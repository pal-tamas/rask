# Rask.Xlsx — an Excel workbook from a list of rows, and back

`Rask.Xlsx` writes an Excel workbook (`.xlsx`) from headings and rows and hands back its bytes — and reads one back
into the same shape, a cell by its heading. It uses nothing but `System.IO.Compression`, `XmlReader` and
hand-written OOXML — **zero dependencies**, and pure managed code, so it runs on a server and in a WebAssembly app
alike (`net11.0` and `net11.0-browser`).

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

## Read a sheet

`Xlsx.Read` hands back the same `Xlsx.Sheet` the writer takes — its `Name`, its `Headings` and its `Rows` — and
every row finds a cell by its heading:

```csharp
using Rask.Workbooks;

Xlsx.Sheet sheet = Xlsx.Read(file);                 // the first sheet, headings on row 1
Xlsx.Sheet fuel = Xlsx.Read(file, "Tankolás");      // a sheet by its name (without regard to case, as Excel compares them)

foreach (var row in sheet.Rows)
{
    var plate = (string?)row["Plate"];
    var litres = (decimal?)row["Litres"];
}
```

`file` is the workbook's `byte[]` (an upload's bytes, `File.ReadAllBytes`, an `HttpClient` download). A row is an
`Xlsx.Row`: a list of its cells by place (`row[2]`) that also finds one by heading (`row["Litres"]`). A heading is
matched **without regard to case and with the whitespace around it trimmed**; one the sheet does not have throws a
`KeyNotFoundException` that lists the headings it does, and one two columns share throws rather than guessing.

### Headings lower on the sheet

A workbook made for people often has a title above its table and headings over two rows, a group heading merged
across the columns it covers. `HeadingsOn` reads the headings from those rows (numbered as Excel numbers them, from
1) and the rows from the one after the last of them:

```csharp
// rows 1-2: a title; rows 3-4: headings, "Tankolás" merged over C3:E3; the data from row 5
var sheet = Xlsx.Read(file).HeadingsOn(3, 4);

sheet.Headings;   // ["Rendszám", "Dátum", "Tankolás Liter", "Tankolás Összeg", "Tankolás Km", "Megjegyzés"]
var litres = (decimal?)row["Tankolás Liter"];
```

A column's heading is its cells on those rows, top to bottom, **joined by a space**: each part trimmed, an empty one
left out. A merged cell's text stands over every column and row it spans, and a cell merged down over several
heading rows (`A3:A4`) is taken once — so `Rendszám` is not `Rendszám Rendszám`.

**A title merged across the top makes every column it covers share one heading.** Read without `HeadingsOn`, a sheet
whose row 1 is a title merged over `A1:B1` has `Headings` `["Title", "Title"]`; `row["Title"]` then throws an
`InvalidOperationException` naming the columns that share it, rather than guessing. Read the headings from the rows
under the title (`HeadingsOn(2)`), or, on a label/value sheet with no headings at all, read the cells by place:
`row[0]`, `row[1]`.

### What a cell reads back as

The writer's own types:

| The cell holds | You get |
|---|---|
| text (shared, inline or rich text) | `string` (Excel's `_x0001_` escapes read back) |
| a number | `decimal` (one beyond a decimal's range, such as `1E+300`, a `double`) |
| `TRUE` / `FALSE` | `bool` |
| a number shown as a date only | `Xlsx.Day` (its `WallClock` keeps a time of day the cell may hold) |
| a number shown with a time | `Xlsx.Moment` |
| a formula | what Excel last worked it out to; an error as its text (`#N/A`) |
| nothing | `null` |

A number is a date when its format shows one: Excel's built-in date and time formats (14–22, 45–47), or a format of
the workbook's own with a day, month, year, hour or second in it outside quotes and `[brackets]`. A date counts days
in Excel's 1900 system, keeping its leap-year bug (day 60 is the 29 February 1900 that never was), or from 1904 in
a workbook saved that way. A row with nothing in it between others reads as a row of `null`s; the empty rows after
the last value are left out. A sheet read back has no `Total`: a written total row is its last row.

### A file that is not what it should be

A file that is not a workbook, or a broken one, throws an `InvalidDataException` that says why; a sheet name it does
not have, an `ArgumentException` that lists the ones it has. Reading is bounded: a part of the file unpacks to at
most 256 MB and a sheet holds at most 10 million cells, so a file made to unpack without end (a zip bomb) is refused
rather than read. Nothing is fetched or expanded from the file: a DTD is refused outright.

## Safety

A text is written as an inline string, so a cell starting with `=`, `+`, `-` or `@` is shown as text and is never a
formula (CSV-injection safe); only an explicit `Xlsx.Formula` is one. Control characters, which XML 1.0 cannot hold,
are written in Excel's own `_x0001_` escape.
