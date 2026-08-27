# Localization

Ship your app in more than one language: dates and numbers in the visitor's format, text in their
language, and `<html lang>` that tells the truth.

```bash
rask new Shop --culture en --culture hu
```

The first language named is the default. To add it to an app you already have, three lines:

```csharp
builder.Services.AddRask(configureCulture: c =>
{
    c.SupportedCultures.Add("en");   // the default
    c.SupportedCultures.Add("hu");
});
```

Until you name a language, **nothing changes**: `<html lang="en">`, no `dir` attribute, and no cost on
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
        Select.OnChangeAsync(e => culture.SetAsync(e.Value ?? "en"))[
            culture.Supported.Select(c => Option.Value(c.Name)[c.NativeName])
        ];
}
```

`SetAsync` switches the session, remembers the choice, and repaints. No reload.

## Right-to-left

A right-to-left culture emits `dir="rtl"` on `<html>`; everything else emits no `dir` at all, because
left-to-right is HTML's default. Use logical CSS properties (`margin-inline-start`, not `margin-left`)
and the layout follows.

## WASM and ICU

A WASM app needs culture data, and Rask does **not** ship it by default, because it is the one part of
this feature you can measure on the download. `rask new --template wasm --culture hu` sets it for you;
add it by hand to an app that grows a second language later:

```xml
<RaskGlobalization>true</RaskGlobalization>
```

Measured on the WASM showcase, publishing the same trimmed app with and without it:

| | raw | brotli (what a host serves) |
| --- | --- | --- |
| without ICU | 12.44 MB | 3.28 MB |
| with ICU | 16.36 MB | 4.33 MB |
| **cost** | **+3.92 MB** | **+1.05 MB (+32%)** |

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
- the WebAssembly SDK otherwise ships a **reduced ICU shard** covering only EFIGS — English, French,
  Italian, German, Spanish. Under it `hu-HU` resolves, does not throw, and formats dates **in
  English**: every check passes and the output is quietly wrong. `RaskGlobalization` requests full ICU
  instead

If your app genuinely only needs EFIGS, set `<WasmIncludeFullIcu>false</WasmIncludeFullIcu>` back and
save the difference.

## Translating the framework's own text

The picker chrome, the not-found page and the error page are translated the same way, with a reserved
catalog whose keys are `RaskString` members:

```jsonc
// Resources/RaskStrings.hu.json
{ "PickerClear": "Törlés", "PickerPreviousMonth": "Előző hónap" }
```

No neutral file: the framework's English lives in its own code, so anything you have not translated
keeps it. A misspelled key is a build error listing the valid names.

## Docker

The scaffolded image is Debian-based (`mcr.microsoft.com/dotnet/aspnet`) and ships ICU, so nothing to
do. If you switch to an Alpine base, set `InvariantGlobalization=false` and install `icu-libs` — a
container without them formats every culture identically.

## See also

- [Diagnostics](diagnostics.md#rask051) — RASK051 and RASK052 in full
- [Bootstrap pickers](bootstrap-pickers.md) — the date/time controls' chrome
- [Accessibility](accessibility.md) — `lang` on a run of text, versus on the document
