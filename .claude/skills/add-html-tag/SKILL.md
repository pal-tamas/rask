---
name: add-html-tag
description: Scaffold a new HTML tag component in Rask.Html. Use whenever adding support for an HTML element (e.g. <dialog>, <details>, <progress>, <video>) to the Rask framework. Creates src/Rask.Html/Components/{Tag}.cs and the matching tests/Rask.Html.Tests/Components/{Tag}Tests.cs asserting exact attribute order; the factory is generated automatically.
---

# add-html-tag

Adds one HTML element. The `Generated.{Tag}(...)` factory is produced by the Roslyn generator —
you only write the component + its test. Follow C# conventions: `sealed`, file-scoped namespace,
nullable enabled, expression-bodied single-line members.

## 1. Component — `src/Rask.Html/Components/{Tag}.cs`
- `public sealed class {Tag} : Element`, override `protected override string TagName => "{tag}";`.
  - **If the tag shares a DOM interface with sibling tags, derive from the matching `Html*Element`
    base instead of `Element`** — these mirror the DOM hierarchy and hold the shared attributes:
    `HtmlMediaElement` (audio/video), `HtmlTableCellElement` (td/th), `HtmlModElement` (ins/del),
    `HtmlTableColElement` (col/colgroup), `HtmlQuoteElement` (q/blockquote), `HtmlHeadingElement`
    (h1–h6), `HtmlTableSectionElement` (thead/tbody/tfoot). A base's `WriteAttributes` runs first, so
    its shared attrs emit before your tag-specific ones; the generator still flattens every inherited
    public prop into the factory. Add a new `Html*Element` base when two tags would otherwise duplicate
    the same attribute set.
- **Void/self-closing** elements (br, img, input, hr, meta, link, …) add `protected override bool SelfClosing => true;`.
- Each tag-specific attribute is a **public mutable property**. Type choice drives the factory:
  - nullable (`string?`, `bool?`, `int?`) → **optional** factory param (default null) — use this for HTML attrs to keep them ergonomic.
  - non-nullable, no initializer → **required** factory param (RASK001).
  - property with an initializer or `[SkipFactory]` → excluded.
- Override `WriteAttributes` only if there are tag-specific attributes: call `base.WriteAttributes(sb)`
  **first** (emits id, class, style, data-*, ref), then `AppendAttr(sb, "name", value)` per attr
  (`AppendUrlAttr` for URL attrs; `AppendAttr(sb, "disabled", null)` for bare boolean attrs).

See `templates/Component.cs`. References: `src/Rask.Html/Components/Span.cs` (simple),
`Br.cs` (void), `Img.cs` (attributes), base `src/Rask.Core/Element.cs`.

Note the split: Rask.Core still declares the handful of tags its own engine constructs — the shell
(`Html`/`Body`/`Head`), the error and not-found pages' markup, and `Button` among them. A NEW tag goes in
`Rask.Html`, so don't take `src/Rask.Core/Components/Button.cs` as the pattern to copy.

## 2. Test — `tests/Rask.Html.Tests/Components/{Tag}Tests.cs`
Two methods, xUnit:
- `Render_NullProps_…` — only `TagName` (and self-close shape) renders.
- `Render_AllPropsSet_…` — **asserts exact attribute order**: id, class, style, data-*, **then**
  tag-specific attrs. Tests assert this ordering — preserve it.

See `templates/ComponentTests.cs`. Reference: `tests/Rask.Html.Tests/Components/ImgTests.cs`.

## 3. Finish
This is a new feature → run the **`rask-ship`** gate (format → warnings-as-errors build → the
unit test you just wrote → CHANGELOG → review → PR).
