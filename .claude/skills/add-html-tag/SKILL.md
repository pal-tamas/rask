---
name: add-html-tag
description: Scaffold a new HTML tag component in Rask.Core. Use whenever adding support for an HTML element (e.g. <dialog>, <details>, <progress>, <video>) to the Rask framework. Creates src/Rask.Core/Components/{Tag}.cs and the matching tests/Rask.Core.Tests/Components/{Tag}Tests.cs asserting exact attribute order; the factory is generated automatically.
---

# add-html-tag

Adds one HTML element. The `Generated.{Tag}(...)` factory is produced by the Roslyn generator —
you only write the component + its test. Follow C# conventions: `sealed`, file-scoped namespace,
nullable enabled, expression-bodied single-line members.

## 1. Component — `src/Rask.Core/Components/{Tag}.cs`
- `public sealed class {Tag} : Element`, override `protected override string TagName => "{tag}";`.
- **Void/self-closing** elements (br, img, input, hr, meta, link, …) add `protected override bool SelfClosing => true;`.
- Each tag-specific attribute is a **public mutable property**. Type choice drives the factory:
  - nullable (`string?`, `bool?`, `int?`) → **optional** factory param (default null) — use this for HTML attrs to keep them ergonomic.
  - non-nullable, no initializer → **required** factory param (RASK001).
  - property with an initializer or `[SkipFactory]` → excluded.
- Override `WriteAttributes` only if there are tag-specific attributes: call `base.WriteAttributes(sb)`
  **first** (emits id, class, style, data-*, ref), then `AppendAttr(sb, "name", value)` per attr
  (`AppendUrlAttr` for URL attrs; `AppendAttr(sb, "disabled", null)` for bare boolean attrs).

See `templates/Component.cs`. References: `src/Rask.Core/Components/Span.cs` (simple),
`Br.cs` (void), `Button.cs` (attributes), base `src/Rask.Core/Element.cs`.

## 2. Test — `tests/Rask.Core.Tests/Components/{Tag}Tests.cs`
Two methods, xUnit:
- `Render_NullProps_…` — only `TagName` (and self-close shape) renders.
- `Render_AllPropsSet_…` — **asserts exact attribute order**: id, class, style, data-*, **then**
  tag-specific attrs. Tests assert this ordering — preserve it.

See `templates/ComponentTests.cs`. Reference: `tests/Rask.Core.Tests/Components/ButtonTests.cs`.

## 3. Finish
This is a new feature → run the **`rask-ship`** gate (format → warnings-as-errors build → the
unit test you just wrote → CHANGELOG → review → PR).
