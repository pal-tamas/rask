# Elements & the DSL

Rask UI is plain C#: you compose components from a small set of **primitives**, a generated **tag
factory** for every HTML (and SVG) element, a uniform set of **universal props** on every tag, and the
`[...]` children indexer to nest them. This guide is the reference for that surface, with a live demo of
each piece.

- [Primitives](#primitives) — `Text`, `Raw`, `Doctype`, sibling fragments via `[...]`, and children from strings
- [Tag factories](#tag-factories) — every HTML element, strongly typed
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

## Tag factories

Every standard HTML element has a generator-emitted factory in `Rask.Html.Components.Generated`.
Tag-specific attributes come first; the universal `Id`/`Class`/`Style`/`Data` trail at the end.

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

Every tag accepts `Id`, `Class`, `Style`, `Data`, and the accessibility props `Role`, `TabIndex`, and
`Aria`. They render in that exact order, ahead of any tag-specific attributes.

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

**Attribute order** is fixed: base props first (`id`, `class`, `style`, `title`, `data-*`, `role`,
`tabindex`, `aria-*`), then tag-specific. Tests enforce it, so the output is predictable for diffing and
DOM tooling:

`Title` is the global `title` attribute — the browser's hover tooltip. Reach for it where a cell shows an
abbreviated value and the exact one belongs behind it (a relative timestamp over the precise instant, a
truncated string over its full text). It is **not** a label: `title` is invisible to touch users,
unreliable with screen readers, and unfocusable, so it may carry supplementary detail but never the only
copy of something the reader needs — use `Aria` for an accessible name.

<!-- demo:props-attribute-order -->

---

## SVG

SVG elements are first-class core components. `svg`, `g`, `path`, the shapes, `text`, gradients and
filters all have typed factories that flow through scoped CSS, keyed lists, and event handlers — **no
`Raw()` required**.

Shapes inside an `<svg>` — presentation attributes (`Fill`, `Stroke`, `StrokeWidth`, `StrokeLinecap`, …)
live on the shared `SvgElement` base, so every shape exposes them as optional factory parameters:

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

Every standard element is a generated factory, composed through the `[...]` children indexer. The
catalog below groups them the way the HTML spec does.

### Text & inline

`a`, `abbr`, `b`, `bdi`, `bdo`, `br`, `cite`, `code`, `data`, `dfn`, `del`, `em`, `i`, `ins`, `kbd`,
`mark`, `q`, `ruby`/`rp`/`rt`, `s`, `samp`, `small`, `span`, `strong`, `sub`, `sup`, `time`, `u`, `wbr`:

<!-- demo:elements-text -->

### Grouping & lists

`p`, `hr`, `pre`, `blockquote`, `ol`/`ul`/`li`, `dl`/`dt`/`dd`, `figure`/`figcaption`, `div`:

<!-- demo:elements-grouping -->

### Sections & headings

`h1`–`h6`, `header`, `footer`, `main`, `section`, `article`, `aside`, `nav`, `address`, `hgroup`:

<!-- demo:elements-sections -->

### Form elements

`form`, `label`, `input`, `button`, `select`/`option`/`optgroup`, `textarea`, `fieldset`/`legend`,
`datalist`, `output`, `progress`, `meter`:

<!-- demo:elements-forms -->

### Table elements

`table`, `caption`, `colgroup`/`col`, `thead`/`tbody`/`tfoot`, `tr`, `th`/`td`:

<!-- demo:elements-tables -->

### Media & embedded

`img`, `picture`/`source`, `audio`, `video`/`track`, `iframe`, `embed`, `object`, `canvas`, `map`/`area`:

<!-- demo:elements-media -->

### Interactive

`details`/`summary`, `dialog`, `menu`:

<!-- demo:elements-interactive -->

### Document & metadata

`html`, `head`, `body`, `title`, `base`, `link`, `meta`, `style`, `script`, `noscript`:

<!-- demo:elements-metadata -->

---

See also: [Getting started](getting-started.md) for building your first component, and
[Best practices](best-practices.md) for production patterns.
