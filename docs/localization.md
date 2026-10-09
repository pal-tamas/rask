# Localization

Ship your app in more than one language: dates and numbers in the visitor's format, text in their
language, and `<html lang>` that tells the truth.

Every scaffolded server app already ships one language, English, listed in `appsettings.json`. Adding a
second is an entry in the list that is already there:

```jsonc
{
  "Rask": {
    "Culture": {
      "SupportedCultures": [ "en", "hu" ]   // the first is the default; add one to ship another
    }
  }
}
```

The first entry is the default a visitor falls back to. **That list is where languages are configured** —
there is no CLI flag for it, because the file is where the answer lives and stays
([#854](https://github.com/pal-tamas/rask/issues/854)). In the environment it is one variable per entry
(`Rask__Cultures__0=en`).

A `configureCulture` callback on `AddRask` still works and runs after the section. The list is appended to
rather than replaced, so a language added in code joins the configured ones:

```csharp
builder.Services.AddRask(configureCulture: c => c.SupportedCultures.Add("hu"));
```

A browser (WebAssembly) app has no `appsettings.json`, so it names its languages in code.

Until you add a second, **nothing changes**: `<html lang="en">`, no `dir` attribute, and no cost on
the render path.

## How a visitor's language is chosen

| Order | Source | Notes |
|---|---|---|
| 1 | `?culture=hu` | An explicit act. Also remembered, so a shared link sticks |
| 2 | the culture cookie | A choice they made earlier |
| 3 | `Accept-Language` / `navigator.languages` | What their browser says they read |
| 4 | your first `SupportedCultures` entry | The default |

A request for a language you ship in another region still works: `hu-HU` is served by `hu`, and `hu` is
served by a supported `hu-HU`. A language you do not ship falls through to the default rather than
being honoured.

**The language is settled before the first render**, on both hosts. That matters more than it sounds:
by the time script could read `navigator.language`, the page has already painted — and if it painted in
the wrong language the visitor would watch it change.

### Why the URL has no language in it

`/products/42` is the same page for everyone. That is a deliberate trade:

- a pasted link works for whoever opens it, in *their* language
- the router, the generated `Url()` / `Go()` helpers and every route value stay culture-neutral
- nothing in your app has to thread a culture through link generation

The cost is that you do not get per-language URLs for search engines. If you need those, you need more
than a prefix — canonical tags, `hreflang` pairs, and a sitemap per language — which is a different
feature with a different design, not a switch to flip.

### The cookie

Rask reads and writes **ASP.NET's own** `.AspNetCore.Culture` cookie, in ASP.NET's own format. So an
app that also calls `UseRequestLocalization()` for its non-Rask endpoints, or that shares a host with
MVC, agrees with them rather than holding a second, conflicting preference.

It is deliberately readable from script — the WASM host reads it before the runtime boots, to stamp
`lang`/`dir` on the document — so it carries a language tag and never anything else.

A choice made with `Set` is written by `Rask.Web.CookieCulturePersistence`, through `document.cookie`
(`Document.SetCookie`), so it is remembered over plain HTTP too. Both hosts register it, so there is nothing to
write; register your own `IRaskCulturePersistence` first to store the choice elsewhere.

## Translating your text

Put a catalog per language in `Resources/`:

```jsonc
// Resources/Strings.en.json — the neutral catalog defines which keys exist
{
  "AppTitle": "Welcome",
  "Greeting": "Hello, {name}!",
  "Home": { "Title": "Dashboard" }
}
```

```jsonc
// Resources/Strings.hu.json
{
  "AppTitle": "Üdvözöljük",
  "Greeting": "Szia, {name}!",
  "Home": { "Title": "Irányítópult" }
}
```

They compile into real members:

```csharp
Div[
    H1[Strings.Home.Title],
    P[Strings.Greeting(user.Name)]
]
```

**A typo is a compile error.** `Strings.Greetng(...)` is CS0117 and the wrong number of arguments is
CS1501 — at the call site, before anything runs. Nothing renders a bare key to a user.

> `Text[Strings.Greeting(name)]` renders **nothing**. `Text` displays its `Value`, not its children —
> write `P[Strings.Greeting(name)]`, a bare child, or `Text.Value(...)`.

### Placeholders

`{name}` takes `object?`. `{count:int}` makes it typed. `{price:decimal:C}` adds a format specifier,
applied in the visitor's culture.

Names, not positions, because **other languages reorder arguments**:

```jsonc
{ "M": "{a} then {b}" }     // en
{ "M": "{b} majd {a}" }     // hu — fine, the same names
```

What is not fine is a different *set* of names: that throws `FormatException` when a Hungarian visitor
first sees the string, so it is [RASK051](diagnostics.md#rask051), an error at build time.

### Counts

A count is dynamic, so "write two keys and pick one" breaks the moment a language has three categories
— and Polish, Russian, Czech and Arabic all do. Write a plural set:

```jsonc
{ "Cart": { "$plural": "count", "one": "{count} item", "other": "{count} items" } }
```

```csharp
Span[Strings.Cart(basket.Count)]
```

Each language supplies the categories **it** distinguishes:

```jsonc
// Resources/Strings.pl.json — Polish, and correctly no "other"
{ "Cart": { "$plural": "n", "one": "{n} plik", "few": "{n} pliki", "many": "{n} plików" } }
```

Polish integers never select `other`; CLDR routes the residual to `many`. A language whose grammar Rask
does not carry is a build error naming it — better than silently applying English rules and shipping
text that reads as broken to every native speaker.

### Missing translations

A key you have not translated yet is a **warning** ([RASK052](diagnostics.md#rask052)) and falls back
to the neutral text, so the page still works. Gate a release on complete translations by promoting it:

```ini
# .editorconfig
dotnet_diagnostic.RASK052.severity = error
```

Or silence it with `= none` while translation is in progress.

<!-- demo:localization-formats -->

## Reading the culture in a component

```csharp
public sealed partial class Receipt : Component
{
    protected override Component Render() =>
        Div[
            P[Total.ToString("C", Culture)],
            P[IssuedOn.ToString("d", Culture)]
        ];
}
```

`Culture` is the visitor's, for formatting. `UICulture` is the language their text is in. `IsRightToLeft`
answers the layout question.

Reading any of them tells the render cache this component depends on the culture, so a language switch
repaints it. **That is why you should read `Culture` rather than `CultureInfo.CurrentCulture`** — the
ambient value is pinned to the same culture during a render, so it formats correctly, but the cache
cannot see that you read it.

## What stays culture-neutral, on purpose

These are **wire formats**, not display formats, and they do not follow the visitor:

- `<input type="date">` and `type="number"` values — the HTML5 wire format is fixed, and **the browser
  localizes the display itself**
- route values and generated URLs
- reconciliation keys, DOM event payloads, and anything else crossing the socket

If you are formatting a value to send somewhere rather than to show someone, pass
`CultureInfo.InvariantCulture` explicitly.

## Switching language at runtime

```csharp
public sealed partial class LanguageMenu(IRaskCulture culture) : Component
{
    protected override Component Render() =>
        Select.OnChange(e => culture.Set(e.Value ?? "en"))[
            culture.Supported.Select(c => Option.Value(c.Name)[c.NativeName])
        ];
}
```

`Set` switches the session, remembers the choice, and repaints. No reload.

**No template scaffolds this, deliberately** ([#854](https://github.com/pal-tamas/rask/issues/854)).
A new project starts with English in `Rask:Cultures`; adding a language is another
entry there. That is the whole configuration surface — there is
no `--culture` flag, because a flag would only restate what the file already says, and it would say it
once at scaffold time while the file goes on being the truth.

*Where* a language control belongs in your chrome is a different question, and it is a design decision
about your app rather than wiring — the same line the styling decisions sit on. So a
scaffolded app negotiates language correctly out of the box, and a visitor can be *sent* to a language
by link, but there is no affordance for choosing one until you add the component above.

That is worth stating rather than leaving to be discovered: negotiation working end to end reads very
much like a switcher being present somewhere. `RaskCultureNegotiator.TrySelect` is kept separate from
`Negotiate` precisely so an explicit pick is honoured regardless of `UseQueryString`, which is what
keeps the menu above at ten lines instead of a feature.

## Right-to-left

A right-to-left culture emits `dir="rtl"` on `<html>`; everything else emits no `dir` at all, because
left-to-right is HTML's default. Use logical CSS properties (`margin-inline-start`, not `margin-left`)
and the layout follows.

## WASM and ICU

A WASM app needs culture data, and Rask does **not** ship it by default, because it is the one part of
this feature you can measure on the download — and the one part `Program.cs` cannot switch on by
itself, since it is an MSBuild property. `rask new --template wasm` scaffolds it **commented out**,
with the reason beside it, so an app that grows a second language later uncomments one line:

```xml
<RaskGlobalization>true</RaskGlobalization>
```

That is also why a browser-WASM app scaffolds no language registration at all, where the server
template scaffolds English: on the server the runtime carries ICU regardless and it costs nothing, and
in the browser it is roughly a megabyte. Configure the languages in `Program.cs` **and** uncomment the
property — catalogs without ICU are a no-op, because the resolver refuses to let a culture pose as
supported when the data is not there, and the app boots with an empty supported list and one warning.

Measured on the WASM showcase, publishing the same trimmed app with and without it:

| | raw | brotli (what a host serves) |
| --- | --- | --- |
| without ICU | 12.44 MB | 3.28 MB |
| with ICU | 16.36 MB | 4.33 MB |
| **cost** | **+3.92 MB** | **+1.05 MB (+32%)** |

Re-measured for [#853](https://github.com/pal-tamas/rask/issues/853) and still right: +3.90 MB raw /
+1.06 MB brotli (+33%) on the current tree. The figures were always the cost of the **shards** — see
below — which is what has shipped all along.

Roughly a third of that is the `icudt*.dat` files themselves; the rest is a larger `dotnet.native.wasm`
and `System.Private.CoreLib`, because turning globalization on brings back runtime code that invariant
mode trims away. That is why localization is opt-in on the browser templates and standard on `server`,
where the runtime already carries ICU and it costs nothing.

Without it every culture formats identically and only the invariant culture resolves — and because
Rask's resolver refuses to let a culture *pose* as supported when the data isn't there, an app that
configures languages without ICU starts with an empty supported-language list and says so once at
startup rather than once per render.

**Translated text works either way** — lookup is keyed on a language tag rather than a `CultureInfo`,
so an app can ship three languages with no ICU at all. Only date/number *formatting* falls back.

One property covers all three halves of this, because each default is a trap on its own:

- `PredefinedCulturesOnly` otherwise stays `true`, and `CultureInfo.GetCultureInfo("hu-HU")` **throws**
  rather than falling back
- the runtime otherwise has **no culture data at all**, so no named culture resolves

### What ships, and the one thing to know about it

A globalized WASM publish carries **three reduced ICU shards**, not one full `icudt.dat`:

```
icudt_EFIGS.<hash>.dat    English, French, Italian, German, Spanish
icudt_CJK.<hash>.dat      Chinese, Japanese, Korean
icudt_no_CJK.<hash>.dat   everything else — including Hungarian, and English
```

The runtime loads **one of them**, chosen at boot, and it chooses from the *visitor's browser*
(`navigator.languages[0]`) rather than from the languages your app ships. So a second language works
for a visitor whose browser is already set to it, and an `en`+`hu` app opened in an **English** browser
loads EFIGS — which has no Hungarian. With `PredefinedCulturesOnly=false`, `hu-HU` then resolves, does
not throw, and formats dates in English.

> **Known limitation**, tracked in [#853](https://github.com/pal-tamas/rask/issues/853). Rask set
> `WasmIncludeFullIcu` intending to ship full ICU and avoid this; the SDK's property is
> `WasmIncludeFullIcuData`, so it was never read. Correcting the spelling turns out **not** to help
> either: that property is honoured only by the `WasmAppBuilder` bundle path, and a Rask app publishes
> through `Microsoft.NET.Sdk.WebAssembly`, whose pipeline has no ICU handling of its own. Measured —
> publishing with the property `true` and `false` produces byte-identical output.
>
> If your app ships more than one language and its speakers may arrive with a different browser
> language, be aware of this. A single-language app is unaffected.

## Translating the framework's own text

Rask draws a little text of its own — the pager's "Showing 1 to 10 of 13 results", a select's "No results
found", a date picker's "Select a date", the names a screen reader hears for a close button. Every one of
them is a `RaskString` key with its English beside it in the code, and is looked up in this order:

1. **your app's** `Resources/RaskStrings.{culture}.json`, if it has the key
2. **the UI kit's own** translation — `Rask.Ui` ships **Hungarian** (`hu`) beside its English
3. the English

So an app that lists `hu` in `SupportedCultures` gets a Hungarian pager, select, date picker, editor toolbar
and data grid with no catalog of its own, and a language switch repaints them like any other text. The kit's
translations apply only to the languages your app says it ships — an app with no `SupportedCultures` keeps
the English whatever the server's or the browser's language is. Month and weekday names, dates and numbers
are not in any catalog: they come from the culture.

### Adding a language, or changing a word

The reserved catalog is an ordinary JSON file whose keys are `RaskString` members. Write the keys you want
and leave the rest: anything you have not translated falls through to the kit's own translation, then to
the English.

```jsonc
// Resources/RaskStrings.hu.json — the kit already speaks Hungarian. This overrides two of its words.
{
  "ConfirmLeaveStay": "Nem",
  "ConfirmLeaveLeave": "Igen"
}
```

```jsonc
// Resources/RaskStrings.de.json — a language the kit does not ship: the pager and the form controls.
{
  "PaginationSummary": "{0}–{1} von {2} Ergebnissen",
  "PaginationPrevious": "« Zurück",
  "PaginationNext": "Weiter »",

  "SelectEmpty": "Keine Ergebnisse",
  "SelectLoading": "Wird geladen…",
  "SelectSearchPlaceholder": "Suchen…",
  "SelectSearchClear": "Suche leeren",
  "SelectClear": "Auswahl aufheben",
  "SelectSelectedSuffix": "ausgewählt",

  "DatePickerPlaceholder": "Datum wählen",
  "DatePickerRangePlaceholder": "Zeitraum wählen",
  "DatePickerConfirm": "Übernehmen",
  "DatePickerCancel": "Abbrechen",
  "CalendarToday": "Heute",
  "PickerPreviousMonth": "Vorheriger Monat",
  "PickerNextMonth": "Nächster Monat",

  "TimePickerPlaceholder": "Uhrzeit wählen",
  "PickerClear": "Löschen",

  "InputClear": "Eingabe löschen",
  "InputCopy": "In die Zwischenablage kopieren",
  "InputTogglePassword": "Passwort ein- oder ausblenden",
  "InputChooseFile": "Datei auswählen",
  "InputChooseFiles": "Dateien auswählen",
  "InputNoFile": "Keine Datei ausgewählt",

  "ModalClose": "Dialog schließen"
}
```

No neutral file: the framework's English lives in its own code. A misspelled key is a build error (RASK051)
listing the valid names.

**A text that carries values numbers them** — `{0}`, `{1}`, `{2}` — so a translation puts them in its own
order, and may give one a .NET format: the Hungarian pager is `"{0}–{1}. találat, összesen {2}"`, and
`"{2:N0}"` would group the total's thousands. The values are written in the visitor's culture. Asking for a
value the text does not carry (`{3}` in the pager's summary), or naming one (`{total}`), is a build error
too. There are no plural forms here: none of these texts changes with its number in English, and a count
stands before a singular in Hungarian.

The kit's own Hungarian is the same kind of file, `src/Rask.Ui/Resources/RaskStrings.hu.json`, compiled by
the same generator. A component library of your own ships translations for the framework texts it draws the
same way — the file, and `<RaskStringsLibrary>true</RaskStringsLibrary>` in its project, which registers
them a layer under the app's catalog instead of in its place.

### Every key

What each key says in English, and where it is drawn. A component's own props still win where it has them —
`Ui.Select.Empty("…")`, `Ui.DatePicker.Placeholder("…")`, `Ui.ConfirmLeave.Stay("…")`: the catalog is what
they say when you set nothing.

| Key | Where | English |
|---|---|---|
| `PickerPreviousMonth` | Calendar | `Previous month` |
| `PickerNextMonth` | Calendar | `Next month` |
| `PickerHour` | Time picker | `Hour` |
| `PickerMinute` | Time picker | `Minute` |
| `PickerSecond` | — (nothing reads it today) | `Second` |
| `PickerClear` | Time picker | `Clear` |
| `NotFoundTitle` | Not-found page | `Page not found` |
| `NotFoundBody` | Not-found page | `No route is registered for ` |
| `NotFoundBackHome` | Not-found page | `Back to home` |
| `ErrorHeading` | Error page | `Something went wrong` |
| `ErrorTryAgain` | Error page | `Try again` |
| `ErrorReload` | Error page | `Reload this page` |
| `PaginationSummary` | Pagination | `Showing {0} to {1} of {2} results` |
| `PaginationPrevious` | Pagination | `« Previous` |
| `PaginationNext` | Pagination | `Next »` |
| `CalendarToday` | Calendar | `Today` |
| `DatePickerPlaceholder` | Date picker | `Select a date` |
| `DatePickerRangePlaceholder` | Date picker | `Select a date range` |
| `DatePickerConfirm` | Date picker | `Select date` |
| `DatePickerCancel` | Date picker | `Cancel` |
| `DatePickerMonth` | Date picker | `Month` |
| `DatePickerDay` | Date picker | `Day` |
| `DatePickerYear` | Date picker | `Year` |
| `DatePickerMonthPlaceholder` | Date picker | `mm` |
| `DatePickerDayPlaceholder` | Date picker | `dd` |
| `DatePickerYearPlaceholder` | Date picker | `yyyy` |
| `DateRangePresetToday` | Date picker | `Today` |
| `DateRangePresetYesterday` | Date picker | `Yesterday` |
| `DateRangePresetThisWeek` | Date picker | `This Week` |
| `DateRangePresetLastWeek` | Date picker | `Last Week` |
| `DateRangePresetLast7Days` | Date picker | `Last 7 Days` |
| `DateRangePresetThisMonth` | Date picker | `This Month` |
| `DateRangePresetLastMonth` | Date picker | `Last Month` |
| `DateRangePresetThisQuarter` | Date picker | `This Quarter` |
| `DateRangePresetLastQuarter` | Date picker | `Last Quarter` |
| `DateRangePresetThisYear` | Date picker | `This Year` |
| `DateRangePresetLastYear` | Date picker | `Last Year` |
| `DateRangePresetLast14Days` | Date picker | `Last 14 Days` |
| `DateRangePresetLast30Days` | Date picker | `Last 30 Days` |
| `DateRangePresetLast3Months` | Date picker | `Last 3 Months` |
| `DateRangePresetLast6Months` | Date picker | `Last 6 Months` |
| `DateRangePresetYearToDate` | Date picker | `Year to Date` |
| `DateRangePresetTomorrow` | Date picker | `Tomorrow` |
| `DateRangePresetNextWeek` | Date picker | `Next Week` |
| `DateRangePresetNext7Days` | Date picker | `Next 7 Days` |
| `DateRangePresetNextMonth` | Date picker | `Next Month` |
| `DateRangePresetNextQuarter` | Date picker | `Next Quarter` |
| `DateRangePresetNextYear` | Date picker | `Next Year` |
| `DateRangePresetNext14Days` | Date picker | `Next 14 Days` |
| `DateRangePresetNext30Days` | Date picker | `Next 30 Days` |
| `DateRangePresetNext3Months` | Date picker | `Next 3 Months` |
| `DateRangePresetNext6Months` | Date picker | `Next 6 Months` |
| `DateRangePresetAllTime` | Date picker | `All Time` |
| `DateRangePresetCustom` | Date picker | `Custom` |
| `SelectLoading` | Select | `Loading...` |
| `SelectEmpty` | Select | `No results found` |
| `SelectSearchPlaceholder` | Select | `Search...` |
| `SelectSearchClear` | Select | `Clear command input` |
| `SelectClear` | Select | `Clear selected` |
| `SelectSelectedSuffix` | Select | `selected` |
| `TimePickerPlaceholder` | Time picker | `Select a time` |
| `TimePickerMeridiem` | Time picker | `AM/PM` |
| `TimePickerHourPlaceholder` | Time picker | `hh` |
| `TimePickerMinutePlaceholder` | Time picker | `mm` |
| `EditorLabel` | Editor | `Rich text editor` |
| `EditorToolbar` | Editor | `Formatting` |
| `EditorBold` | Editor | `Bold` |
| `EditorItalic` | Editor | `Italic` |
| `EditorStrike` | Editor | `Strikethrough` |
| `EditorUnderline` | Editor | `Underline` |
| `EditorBullet` | Editor | `Bullet list` |
| `EditorOrdered` | Editor | `Ordered list` |
| `EditorBlockquote` | Editor | `Blockquote` |
| `EditorCode` | Editor | `Code` |
| `EditorHighlight` | Editor | `Highlight` |
| `EditorSubscript` | Editor | `Subscript` |
| `EditorSuperscript` | Editor | `Superscript` |
| `EditorUndo` | Editor | `Undo` |
| `EditorRedo` | Editor | `Redo` |
| `EditorLink` | Editor | `Insert link` |
| `EditorUnlink` | Editor | `Unlink` |
| `EditorAlign` | Editor | `Align` |
| `EditorAlignLeft` | Editor | `Left` |
| `EditorAlignCenter` | Editor | `Center` |
| `EditorAlignRight` | Editor | `Right` |
| `EditorHeading` | Editor | `Styles` |
| `EditorHeadingText` | Editor | `Text` |
| `EditorHeading1` | Editor | `Heading 1` |
| `EditorHeading2` | Editor | `Heading 2` |
| `EditorHeading3` | Editor | `Heading 3` |
| `InputClear` | Input | `Clear input` |
| `InputCopy` | Input | `Copy to clipboard` |
| `InputTogglePassword` | Input | `Toggle password visibility` |
| `InputChooseFile` | Input | `Choose file` |
| `InputChooseFiles` | Input | `Choose files` |
| `InputNoFile` | Input | `No file chosen` |
| `ModalClose` | Modal | `Close modal` |
| `ConfirmLeaveStay` | Confirm leave | `Stay` |
| `ConfirmLeaveLeave` | Confirm leave | `Leave` |
| `OtpCharacter` | OTP input | `Character {0} of {1}` |
| `SliderRangeStart` | Slider | `{0} start range` |
| `SliderRangeEnd` | Slider | `{0} end range` |
| `RatingNone` | Rating | `No rating` |
| `RatingValue` | Rating | `{0} of {1}` |
| `SidebarToggle` | Sidebar | `Toggle sidebar` |
| `SidebarSearch` | Sidebar | `Search` |
| `CommandEmpty` | Command | `No results` |
| `DataGridColumns` | Data grid | `Columns` |
| `DataGridMoveUp` | Data grid | `Move up` |
| `DataGridMoveDown` | Data grid | `Move down` |
| `DataGridGrouping` | Data grid | `Grouping` |
| `DataGridGroupingHint` | Data grid | `Group by a column with its header button.` |
| `DataGridMoveGroupLeft` | Data grid | `Move group left` |
| `DataGridMoveGroupRight` | Data grid | `Move group right` |
| `DataGridUngroup` | Data grid | `Stop grouping by {0}` |
| `DataGridGroupBy` | Data grid | `Group by {0}` |
| `DataGridExpandGroup` | Data grid | `Expand group` |
| `DataGridCollapseGroup` | Data grid | `Collapse group` |
| `DataGridExpand` | Data grid | `Expand` |
| `DataGridSelectAll` | Data grid | `Select all rows on this page` |
| `DataGridEmpty` | Data grid | `Nothing to show.` |
| `DataGridSelectRow` | Data grid | `Select row` |
| `DataGridExpandRow` | Data grid | `Expand row` |
| `DataGridCollapseRow` | Data grid | `Collapse row` |
| `DiffHandle` | Diff | `Compare` |
| `FilterReset` | Filter | `All` |
| `FieldValidating` | Form field | `Checking…` |
| `FileItemRemove` | File upload | `Remove file` |

## Docker

The scaffolded image is Debian-based (`mcr.microsoft.com/dotnet/aspnet`) and ships ICU, so nothing to
do. If you switch to an Alpine base, set `InvariantGlobalization=false` and install `icu-libs` — a
container without them formats every culture identically.

## See also

- [Diagnostics](diagnostics.md#rask051) — RASK051 and RASK052 in full
- [Accessibility](accessibility.md) — `lang` on a run of text, versus on the document
