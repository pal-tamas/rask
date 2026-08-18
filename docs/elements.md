# Elements & the DSL

Rask UI is plain C#: you compose components from a small set of **primitives**, a generated **tag
entry** for every HTML (and SVG) element, a uniform set of **universal props** on every tag, and the
`[...]` children indexer to nest them. This guide is the reference for that surface, with a live demo of
each piece.

- [Primitives](#primitives) — `Text`, `Raw`, `Doctype`, sibling fragments via `[...]`, and children from strings
- [Tag entries](#tag-entries) — every HTML element, strongly typed
- [Universal props](#universal-props) — `Id`/`Class`/`Style`/`Data`/`Aria` on every tag
- [SVG](#svg) — typed SVG components, no `Raw()`
- [HTML elements](#html-elements) — the full element catalog, by category

---

## Primitives

Three primitives sit beneath every Rask page: **`Text`**, **`Raw`**, and **`Doctype`**. Everything else is
built out of them — plus the `[...]` collection expression, which groups siblings with no wrapping tag.

`Text` HTML-encodes its value — `<` and `&` render as literal characters, never parsed as markup:

<!-- demo:primitives-text -->

`Raw` is the escape hatch: verbatim, un-encoded HTML. Use it when you control the source (Markdown
output, sanitised snippets) — **never on user input**.

<!-- demo:primitives-raw -->

> **Security:** `Raw` skips all HTML encoding. Never feed it untrusted strings — sanitize, or use `Text`.

A bare `[...]` collection expression returns multiple siblings with no surrounding tag — a `Component`
in its own right, so it's what `Render()` returns when a component has more than one root — a heading
and its paragraph, a layout's header/main/footer:

<!-- demo:primitives-fragment -->

`Doctype()` emits exactly `<!DOCTYPE html>` — special-cased, with no attributes, children, or wrapper.
An app's pages don't need it (Rask emits the doctype and the rest of the document around the root
component — see [the document and the `Head` override](getting-started.md#7-the-document-and-the-head-override));
reach for it when you build a document by hand, for `ToHtml()` or an email body:

<!-- demo:primitives-doctype -->

A bare `string` is a valid child, so text flows into the `[...]` indexer alongside elements (it's
encoded exactly like `Text`):

<!-- demo:primitives-children -->

---

## Tag entries

Every standard HTML element has a generator-emitted chain entry: name it, dot onto it, nest with `[…]`.
Tag-specific steps and the universal `Id`/`Class`/`Style`/`Data` steps sit side by side on every tag.

Text & semantic elements:

<!-- demo:tags-text -->

Form elements:

<!-- demo:tags-form -->

Tables:

<!-- demo:tags-table -->

Media:

<!-- demo:tags-media -->

Void elements (`Br`, `Hr`, `Img`, `Meta`, `Link`, `Input`, …) have `SelfClosing => true` and never
accept children:

<!-- demo:tags-void -->

---

## Universal props

Every tag accepts `Id`, `Class`, `Style`, `Title`, `Data`, the accessibility props `Role`, `TabIndex`
and `Aria`, the rest of HTML's global attributes (`Lang`, `Dir`, `Hidden`, `Inert`, `Popover`,
`ContentEditable`, `Spellcheck`, `Translate`, `Draggable`), and `Attributes` — the verbatim escape hatch
for anything not named here. They render in a fixed order, ahead of any tag-specific attributes.

`Id`, `Class`, `Style`:

<!-- demo:props-id-class-style -->

`Data` — expands to `data-*` attributes; a null value renders as a bare attribute (e.g. `data-new`),
the same way boolean attributes like `disabled` work. Name the pair directly, or pass several:

```csharp
Div.Data("rask-no-restore")                                            // bare: data-rask-no-restore
Div.Data("test-id", "primary")
Div.Data(("test-id", "primary"), ("state", "idle"))
Div.Data(new Dictionary<string, string?> { ["test-id"] = "primary" })   // still accepted
```

The name-only form is the *bare* attribute, not an empty one — `.Data("flag")` renders `data-flag`
and `.Data("flag", "")` renders `data-flag=""`, which are different attributes.

Prefer the pair form over a dictionary. It is not only shorter: a `Dictionary` for one attribute is
three allocations — the dictionary, its bucket array and its entry array — and a chain step re-assigns
its property on every render, so that is a per-render cost on every element carrying one. The pair
form is a single object the element writer knows by type and writes without materialising an
enumerator. Measured over 100 elements each carrying one `data-*`: **80.7 KB → 63.52 KB, alloc ratio
0.79**, and ~11% faster. With three attributes each it is still ahead (87.15 KB → 75.43 KB).

<!-- demo:props-data -->

`Aria`, `Role`, `TabIndex` — `Aria` is the `data-*` model applied to ARIA (each entry expands to
`aria-{key}`, value HTML-encoded, null → bare attribute), and takes the same three forms
(`Span.Aria("label", "Close")`); `Role` and `TabIndex` are typed because they aren't `aria-*`
attributes. See the [accessibility guide](accessibility.md) and the RASK023 img-alt analyzer.

<!-- demo:props-aria -->

### The rest of the global attributes

`Lang` and `Dir` are the two that matter most, and they are why this section exists: before them, `lang`
was reachable on `<html>` only — so the page's language worked and a phrase inside it did not. Marking a
run of text in another language is [WCAG 3.1.2 *Language of Parts*][wcag312],
and without it a screen reader reads a French quotation with English phonetics:

```csharp
P["The exhibition is called ", Span.Lang("fr")["Les Demoiselles"], "."]
Span.Dir("auto")[userSuppliedName]   // "auto" when you don't know the language at render time
```

`Hidden` hides an element from every presentation *including assistive technology* — prefer it to a
display-none class, which hides it visually while leaving it in the accessibility tree. `Inert` makes a
subtree unfocusable and unreachable, which is the correct primitive behind a modal: mark everything
outside the dialog inert and focus cannot escape it.

`Popover` pairs with `Button.PopoverTarget` for a popover the browser opens, dismisses and focuses with
no JavaScript on either side. `ContentEditable` is a string rather than a `bool?` because
`"plaintext-only"` is the value most editors actually want. `Spellcheck` and `Translate` are enumerated
rather than bare booleans, so `false` renders explicitly (`translate` spells its values `yes`/`no`).

```csharp
Div.Hidden(true)                     // hidden
Div.Inert(true)                      // inert
Div.Popover("auto")                  // popover="auto"
Div.ContentEditable("plaintext-only")
Span.Translate(false)                // translate="no" — a product name, a username, a code sample
```

### `Attributes` — the escape hatch

Everything the class does not name: microdata (`itemscope`/`itemprop`), `nonce`, `part`/`exportparts`,
`accesskey`, `slot`, `inputmode`, and anything vendor or experimental. Entries emit verbatim as
`{key}="{value}"` with the value HTML-encoded, and a null value renders the attribute bare, exactly like
`Data`:

```csharp
Div.Attributes(new() { ["itemscope"] = null, ["itemtype"] = "https://schema.org/Person" })
Div.Attributes(new() { ["inputmode"] = "decimal" })
```

**Prefer a typed property wherever one exists** — it is checked, documented and discoverable, and for
`Hidden`/`Inert` it is also free, where a dictionary is an allocation. `lang`, `dir`, `hidden`, `inert`,
`popover`, `contenteditable`, `spellcheck` and `translate` all have typed properties now (above), so
reach for `Attributes` for the rest. Nothing here is validated or de-duplicated: naming an attribute a
typed property already emits renders it twice and the browser takes the first.

`Attributes` renders last within the universal block, so a typed property always wins the ordering
argument.

<!-- demo:props-attributes -->

### What the globals cost

One reference per node, and nothing at all on the static path.

`Hidden` and `Inert` are two bits each of the flags byte every component already carries, so they are
free. The other six share **one** reference on the lazy live state — a side object allocated only by an
element that actually names one of them — rather than a typed field each, because that state is
allocated per node on a mounted page and a field there is paid for by every node of every live session.

Measured against the commit before them: a static render is unchanged (35.31 KB either way), since a
plain element keeps its live state null; a live render grows by 8 B per node for the single added
reference. `Element.WriteAttributes` reads the side object once into a local rather than once per
attribute, so an element naming no global does *less* work than a typed-field-each layout would have.


**Attribute order** is fixed: `id`, `class`, `style`, `title`, the plain globals (`lang`, `dir`,
`hidden`, `inert`, `popover`, `contenteditable`, `spellcheck`, `translate`), `data-*`, `role`,
`tabindex`, `aria-*`, then `Attributes`, then tag-specific. Tests enforce it, so the output is
predictable for diffing and DOM tooling:

`Title` is the global `title` attribute — the browser's hover tooltip. Reach for it where a cell shows an
abbreviated value and the exact one belongs behind it (a relative timestamp over the precise instant, a
truncated string over its full text). It is **not** a label: `title` is invisible to touch users,
unreliable with screen readers, and unfocusable, so it may carry supplementary detail but never the only
copy of something the reader needs — use `Aria` for an accessible name.

<!-- demo:props-attribute-order -->

---

## SVG

SVG elements are first-class core components. `svg`, `g`, `path`, the shapes, `text`, gradients and
filters all have typed entries that flow through scoped CSS, keyed lists, and event handlers — **no
`Raw()` required**.

Shapes inside an `<svg>` — presentation attributes (`Fill`, `Stroke`, `StrokeWidth`, `StrokeLinecap`, …)
live on the shared `SvgElement` base, so every shape exposes them as optional chain steps:

<!-- demo:svg-shapes -->

Gradients via `<defs>` and `<linearGradient>` (the Rask brand mark itself is built this way); a nested
`SvgTitle` gives the graphic its accessible name:

<!-- demo:svg-gradient -->

Clickable shapes — `OnClick` works on any element; the selection re-renders live over the same transport
as the rest of the page:

<!-- demo:svg-clickable -->

Text with `<text>` and `<tspan>` — `SvgText` is the `<text>` tag (renamed to avoid colliding with the
`Text` primitive); `Tspan` styles a run inside it:

<!-- demo:svg-text -->

---

## HTML elements

Every standard element is a generated chain entry, composed through the `[...]` children indexer. The
catalog below groups them the way the HTML spec does, and each tag links to its MDN reference.

You rarely need to leave the editor for that reference, though: **every element component documents
itself, and the documentation is carried onto the chain**. Hovering `Video` says what `<video>` is
and links the same MDN page, and each step carries its own description — so `Meter`'s
`Low`/`High`/`Optimum`, `Track`'s `Kind`, and `Iframe`'s `Sandbox` explain themselves at the call site
rather than sending you to a search engine.

### Text & inline

[`a`][a], [`abbr`][abbr], [`b`][b], [`bdi`][bdi], [`bdo`][bdo], [`br`][br], [`cite`][cite], [`code`][code], [`data`][data], [`dfn`][dfn], [`del`][del], [`em`][em], [`i`][i], [`ins`][ins], [`kbd`][kbd],
[`mark`][mark], [`q`][q], [`ruby`][ruby]/[`rp`][rp]/[`rt`][rt], [`s`][s], [`samp`][samp], [`small`][small], [`span`][span], [`strong`][strong], [`sub`][sub], [`sup`][sup], [`time`][time], [`u`][u], [`var`][var], [`wbr`][wbr]:

<!-- demo:elements-text -->

### Grouping & lists

[`p`][p], [`hr`][hr], [`pre`][pre], [`blockquote`][blockquote], [`ol`][ol]/[`ul`][ul]/[`li`][li], [`dl`][dl]/[`dt`][dt]/[`dd`][dd], [`figure`][figure]/[`figcaption`][figcaption], [`div`][div]:

<!-- demo:elements-grouping -->

### Sections & headings

[`h1`][h1]–[`h6`][h6], [`header`][header], [`footer`][footer], [`main`][main], [`section`][section], [`article`][article], [`aside`][aside], [`nav`][nav], [`address`][address], [`hgroup`][hgroup]:

<!-- demo:elements-sections -->

### Form elements

[`form`][form], [`label`][label], [`input`][input], [`button`][button], [`select`][select]/[`option`][option]/[`optgroup`][optgroup], [`textarea`][textarea], [`fieldset`][fieldset]/[`legend`][legend],
[`datalist`][datalist], [`output`][output], [`progress`][progress], [`meter`][meter]:

<!-- demo:elements-forms -->

### Table elements

[`table`][table], [`caption`][caption], [`colgroup`][colgroup]/[`col`][col], [`thead`][thead]/[`tbody`][tbody]/[`tfoot`][tfoot], [`tr`][tr], [`th`][th]/[`td`][td]:

<!-- demo:elements-tables -->

### Media & embedded

[`img`][img], [`picture`][picture]/[`source`][source], [`audio`][audio], [`video`][video]/[`track`][track], [`iframe`][iframe], [`embed`][embed], [`object`][object], [`canvas`][canvas], [`map`][map]/[`area`][area]:

<!-- demo:elements-media -->

### Interactive

[`details`][details]/[`summary`][summary], [`dialog`][dialog], [`menu`][menu]:

<!-- demo:elements-interactive -->

### Document & metadata

[`html`][html], [`head`][head], [`body`][body], [`title`][title], [`base`][base], [`link`][link], [`meta`][meta], [`style`][style], [`script`][script], [`noscript`][noscript]:

<!-- demo:elements-metadata -->

---

See also: [Getting started](getting-started.md) for building your first component, and
[Best practices](best-practices.md) for production patterns.

<!-- MDN reference links for the element catalog above. Every element component also carries
     its own MDN link in its XML docs, so the same reference is one hover away in the IDE. -->

[a]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/a
[abbr]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/abbr
[address]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/address
[area]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/area
[article]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/article
[aside]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/aside
[audio]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/audio
[b]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/b
[base]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/base
[bdi]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/bdi
[bdo]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/bdo
[blockquote]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/blockquote
[body]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/body
[br]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/br
[button]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/button
[canvas]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/canvas
[caption]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/caption
[cite]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/cite
[code]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/code
[col]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/col
[colgroup]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/colgroup
[data]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/data
[datalist]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/datalist
[dd]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dd
[del]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/del
[details]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/details
[dfn]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dfn
[dialog]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dialog
[div]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/div
[dl]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dl
[dt]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dt
[em]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/em
[embed]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/embed
[fieldset]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/fieldset
[figcaption]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/figcaption
[figure]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/figure
[footer]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/footer
[form]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/form
[h1]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/Heading_Elements
[h6]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/Heading_Elements
[head]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/head
[header]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/header
[hgroup]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/hgroup
[hr]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/hr
[html]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/html
[i]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/i
[iframe]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe
[img]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/img
[input]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/input
[ins]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ins
[kbd]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/kbd
[label]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/label
[legend]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/legend
[li]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/li
[link]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/link
[main]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/main
[map]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/map
[mark]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/mark
[menu]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/menu
[meta]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meta
[meter]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meter
[nav]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/nav
[noscript]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/noscript
[object]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/object
[ol]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ol
[optgroup]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/optgroup
[option]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/option
[output]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/output
[p]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/p
[picture]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/picture
[pre]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/pre
[progress]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/progress
[q]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/q
[rp]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/rp
[rt]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/rt
[ruby]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ruby
[s]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/s
[samp]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/samp
[script]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/script
[section]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/section
[select]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/select
[small]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/small
[source]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/source
[span]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/span
[strong]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/strong
[style]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/style
[sub]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/sub
[summary]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/summary
[sup]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/sup
[table]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/table
[tbody]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/tbody
[td]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/td
[textarea]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/textarea
[tfoot]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/tfoot
[th]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/th
[thead]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/thead
[time]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/time
[title]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/title
[tr]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/tr
[track]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/track
[u]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/u
[var]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/var
[ul]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ul
[video]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/video
[wbr]: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/wbr
[wcag312]: https://www.w3.org/WAI/WCAG22/Understanding/language-of-parts
