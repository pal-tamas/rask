# Bootstrap — utility classes

Bootstrap's utility classes are exposed by [`Rask.Bootstrap`](bootstrap.md) as **typed string
tokens**, grouped by family, composed into a `Class` with `Bs.Join(...)` (it skips null/empty and
returns `null` when nothing is present, so it leaves `Class` unset rather than emitting `class=""`):

```csharp
BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4)))
Div(Class: Bs.Join(Display.Flex(), Flex.Gap(2), Flex.Justify(BsJustify.Between)))
```

Spacing, display, flex and text-alignment helpers take an optional **responsive breakpoint** `Bp`
(`Bp.Sm/Md/Lg/Xl/Xxl`), which inserts the Bootstrap infix:

```csharp
Bs.Join(Display.Flex(Bp.Lg), Margin.Bottom(4, Bp.Md))   // → "d-lg-flex mb-md-4"
```

## Groups

| Group | Members → emitted class |
|---|---|
| `Shadow` | `None` `Sm` `Default` `Lg` → `shadow-none/-sm/shadow/shadow-lg` |
| `Border` | `All` `None` `Top/End/Bottom/Start` (+`*None`) → `border` `border-0` `border-top` …; `Color(BsColor)` → `border-{color}` |
| `Margin` | `All/Top/Bottom/Start/End/X/Y(int, Bp?)` → `m{side}-{bp?}-{n}`; `XAuto` `StartAuto` `EndAuto` |
| `Padding` | `All/Top/Bottom/Start/End/X/Y(int, Bp?)` → `p{side}-{bp?}-{n}` |
| `Display` | `None/Inline/InlineBlock/Block/Flex/InlineFlex/Grid(Bp?)` → `d-{bp?}-{value}` |
| `Flex` | `Row/Column(+Reverse)/Wrap/Nowrap(Bp?)` `Fill` `Grow(int)` `Shrink(int)` `Gap(int, Bp?)` `Justify(BsJustify, Bp?)` `Align(BsAlign, Bp?)` |
| `Rounded` | `Default` `None` `Pill` `Circle` `Top/End/Bottom/Start` `Size(int)` |
| `Txt` | `Start/Center/End(Bp?)` `Color(BsColor)` `Muted` `Truncate/Wrap/Nowrap/Break` `Uppercase/Lowercase/Capitalize` `DecorationNone/Underline` |
| `Font` | `Bold/Bolder/Semibold/Medium/Normal/Light/Lighter` `Italic/NotItalic` `Size(int)` (→ `fw-*`, `fst-*`, `fs-{n}`) |
| `Sizing` | `W(int)` `H(int)` `WAuto` `HAuto` `MaxW100` `MaxH100` `VW100` `VH100` `MinVW100` `MinVH100` |
| `Position` | `Static/Relative/Absolute/Fixed/Sticky` `Top0/Top50/Bottom0/Start0/…` `TranslateMiddle(+X/Y)` |
| `Bg` | `Color(BsColor)` `Body` `BodyTertiary` `White` `Transparent` |

> The text group is named `Txt` (not `Text`) to avoid clashing with the core `Text` node component.

<!-- demo:bootstrap-utilities -->
