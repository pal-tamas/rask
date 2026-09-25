---
name: add-html-tag
description: Add or change an HTML element in Rask.Core. Use whenever an HTML element, attribute or tag is missing or wrong (e.g. a new <selectedcontent>, a renamed attribute). Elements are generated from MDN data, so this means refreshing the snapshot, and writing a hand partial only for behaviour MDN cannot know (typed binding, events, an RDFa attribute).
---

# add-html-tag

**Elements are not written by hand any more.** Every HTML element type, its base, its `[Tag]`s (the chain
entries) and its attribute properties are generated at build time from `src/Rask.Core/Dom/mdn.snapshot.json`
by `src/Rask.Core/Dom/Rask.Dom.targets` (the emitter is `src/Rask.Dom.Tasks/RaskDomTasks.cs`). Types take
MDN's names (`HTMLAnchorElement`), tags share types the way the DOM shares interfaces (`h1`–`h6` are one
`HTMLHeadingElement`), entries keep tag names (`A`, `H1`), and attributes are PascalCase IDL names (`ColSpan`).

## 1. A tag or attribute is missing → refresh MDN
```bash
scripts/mdn/refresh.sh        # latest stable MDN data, latest LTS Node
git diff --stat src/Rask.Core/Dom/mdn.snapshot.json
```
A local build does this by itself at most once a day. If MDN still lacks it, it does not ship in two of
Chrome/Firefox/Safari (desktop or mobile), or MDN marks it deprecated — then Rask does not offer it either.

## 2. Behaviour MDN cannot know → a hand partial
Add `src/Rask.Core/Components/HTML{Name}Element.cs` as a `partial` of the generated type:
- A member it declares is **not generated** (the emitter reads the partials). Write the attributes it owns in
  `partial void WriteOwnedAttributes(StringBuilder sb)` (after the generated ones) or
  `WriteOwnedAttributesFirst` (before them). Examples: `HTMLMetaElement.cs` (Open Graph's `property`),
  `HTMLMediaElement.cs` (media events).
- A **typed control** is `public sealed partial class HTML{Name}Element<T> : HTML{Name}Element, IFormControl<T>`;
  the emitter then makes the MDN type abstract and puts the `[Tag]` on the typed one (`HTMLInputElement.cs`).
- Rask's own policy (URL sanitizing, omitted attributes, aliases like `For`/`Class`) lives in the emitter's
  named tables, never in scattered overrides.

## 3. Tests
`tests/Rask.Core.Tests/Dom/GeneratedAttributeTests.cs` renders every generated attribute of every tag against the
snapshot, so a refreshed tag is covered with no new test. Behaviour in a partial gets its own test in
`tests/Rask.Core.Tests/Components/`. Then run the **`rask-ship`** gate.
