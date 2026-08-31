# Localization

Ship your app in more than one language: dates and numbers in the visitor's format, text in their
language, and `<html lang>` that tells the truth.

Every scaffolded server app already ships one language, English, registered in `Program.cs`. Adding a
second is a line in the block that is already there:

```csharp
builder.Services.AddRask(configureCulture: c =>
{
    c.SupportedCultures.Add("en");   // the default
    c.SupportedCultures.Add("hu");   // add another to ship another
});
```

The first entry is the default a visitor falls back to. **This is the only place languages are
configured** — there is no CLI flag for it, because the file is where the answer lives and stays
([#854](https://github.com/pal-tamas/rask/issues/854)).

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

**No template scaffolds this, deliberately** ([#854](https://github.com/pal-tamas/rask/issues/854)).
A new project starts with English and the registration above in `Program.cs`; adding a language is
another `c.SupportedCultures.Add(...)` line there. That is the whole configuration surface — there is
no `--culture` flag, because a flag would only restate what the file already says, and it would say it
once at scaffold time while the file goes on being the truth.

*Where* a language control belongs in your chrome is a different question, and it is a design decision
about your app rather than wiring — the same line `--auth` and the styling flags sit on. So a
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
- [Accessibility](accessibility.md) — `lang` on a run of text, versus on the document
