# Rask.Ui

**Every [daisyUI](https://daisyui.com) component, as a typed C# component** for the
[Rask](https://github.com/pal-tamas/rask) One Person Framework — the operator console, the landing
site and the docs showcase all draw with these.

- **The whole daisyUI vocabulary, typed.** Colour, fill and size are independent enums that compose,
  so an outlined error button needs no member of its own. daisyUI's own words, deliberately: its
  documentation is the documentation for everything these components render.
- **The stylesheet comes with it.** Tailwind scans the project it runs in, so a compiled library's
  class names are invisible to *your* Tailwind build and would emit nothing. This package compiles its
  own sheet — daisyUI included — and hands it to you. No npm install, no Tailwind configuration, no
  `_content/` path to map.
- **Ships no JavaScript — but for the rich text editor,** whose engine (Tiptap) is one script a page fetches only when an editor mounts. Where the platform can own the interaction it does: a dialog is a real modal
  `<dialog>` opened by an invoker command, so the browser supplies the top layer, the inert page, Escape
  and a native backdrop. Where it cannot, the component asks Rask's runtime by attribute (a click outside
  a modal, the page held still behind it), or the state is a field in C# and redraws through the live diff.
- **Mobile-first, not merely responsive.** Every control takes a 44px touch target below `sm`; a
  dialog is a bottom sheet on a phone and a centred card above it, because a centred dialog at 360px
  either overflows or shrinks its content past reading.
- **Every control has a name.** A label is required rather than optional, and it becomes the
  accessible name rather than a placeholder — which disappears the moment typing starts.
- **Dark mode, and re-skinned by tokens rather than overrides.** `Ui.AppearanceScript` puts a `dark`
  class on `<html>` before the first paint (light, dark or system, remembered); re-point
  `--color-zinc-*` and the accent variables in your own `@theme` and every component follows.

## Use

```bash
dotnet add package Rask.Ui
```

**An app that runs Tailwind compiles the kit into its own stylesheet** — one sheet, as a Flux app has.
One line in `Styles/app.css`, in place of `@import "tailwindcss";`:

```css
@import "./vendor/rask-ui.css";
```

That is Tailwind, the kit's theme and `dark` variant, and every class its components write, compiled
beside the classes you write. The build puts the files in `Styles/vendor/` (no npm, no `node_modules`;
do not commit them), and on `RaskApp` (`Rask.Server`) or the WASM host (`Rask.Wasm`) that is all: the
host links your `css/app.css` and turns the theme scope on. A hand-wired host does both itself:

```csharp
protected override Component? HeadAssets =>
[
    Link.Rel("stylesheet").Href("/css/app.css"),   // the ONE sheet: Tailwind, the kit, your classes
];

// Nothing in the kit has a colour until an ancestor carries this.
protected override Component Shell(Component head, Component body) =>
    Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, body];
```

**A surface with no Tailwind build of its own** takes the precompiled sheet instead: set
`<RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>` and link `UiStylesheet.Href()`, or inline
`UiStylesheet.Css`. Never both — two sheets each carry an `@layer utilities` ranked by link order
alone, so an app's `bg-white` beats the kit's `dark:bg-zinc-800`, and the build stops on the pairing.

```csharp
protected override Component? Render() =>
[
    Ui.Button.Primary.Icon(Ui.IconName.Check)["Save"],

    // No handler: the trigger's button opens the modal of that name, as the browser's own modal dialog.
    Ui.ModalTrigger.Name("confirm")[Ui.Button["Delete"]],
    Ui.Modal.Name("confirm")[
        P["This cannot be undone."],
        Ui.ModalClose[Ui.Button["Cancel"]]
    ],

    // Every data-input control is an IFormControl<T>: Value opens the controlled chain and Bind the
    // bound one, and the opening step fixes the value type and the mode together.
    Ui.Input.Value(_email).Label("Email").Type(InputType.Email)
           .Tone(_email.Contains('@') ? null : Ui.Tone.Error)
           .OnChange(value => _email = value),

    Ui.Accordion.Exclusive()[
        Ui.AccordionItem.Heading("Shipping")["Two working days."],
        Ui.AccordionItem.Heading("Payment")["Card or transfer."]
    ],
];
```

Re-skin by redefining a token in your own `@theme`, after the import. The kit itself carries **no
preflight** and no `html`/`body` rules — your application owns its document; in an app's sheet the
reset is Tailwind's own, which the import brings.

## What is in it

| | |
| --- | --- |
| Actions | `Ui.Button` `Ui.ButtonGroup` `Ui.Dropdown` `Ui.Menu` `Ui.Navmenu` `Ui.Context` `Ui.Modal` `Ui.ModalTrigger` `Ui.ModalClose` `Ui.Swap` `Ui.Fab` |
| Data display | `Ui.Accordion` `Ui.AccordionItem` `Ui.Avatar` `Ui.Aura` `Ui.Badge` `Ui.BadgeClose` `Ui.Card` (`Ui.CardHeader` `Ui.CardHeading` `Ui.CardSubheading` `Ui.CardActions` `Ui.CardBody` `Ui.CardFooter` `Ui.CardBleed`) `Ui.Carousel` `Ui.ChatBubble` `Ui.Countdown` `Ui.Diff` `Ui.Empty` `Ui.Hover3d` `Ui.HoverGallery` `Ui.Kanban` (with `Ui.KanbanColumn` `Ui.KanbanColumnHeader` `Ui.KanbanColumnCards` `Ui.KanbanColumnFooter` `Ui.KanbanCard`) `Ui.Kbd` `Ui.List` `Ui.Stat` `Ui.StatusDot` `Ui.Table` (with `Ui.TableColumns` `Ui.TableColumn` `Ui.TableRows` `Ui.TableRow` `Ui.TableCell`) `Ui.DataGrid` `Ui.Tree` `Ui.TextRotate` `Ui.Timeline` |
| Navigation | `Ui.Breadcrumbs` `Ui.Dock` `Ui.Link` `Ui.NavList` `Ui.Navbar` `Ui.Pagination` `Ui.Steps` `Ui.Tabs` |
| Feedback | `Ui.Callout` `Ui.Loading` `Ui.Progress` `Ui.Skeleton` `Ui.SkeletonLine` `Ui.SkeletonGroup` `Ui.Toast` `Ui.ToastGroup` `Ui.Tooltip` `Ui.TooltipContent` (Flux's tooltip: hover, focus or `Toggleable`, wired to its trigger for screen readers) |
| Data input | `Ui.Input` `Ui.InputGroup` `Ui.InputGroupPrefix` `Ui.InputGroupSuffix` `Ui.Textarea` `Ui.Select` (`Ui.SelectOption` `Ui.SelectGroup` `Ui.SelectOptionCreate` `Ui.SelectOptionEmpty` `Ui.SelectButton` `Ui.SelectInput` `Ui.SelectSearch`) `Ui.Autocomplete` (`Ui.AutocompleteItem`) `Ui.Pillbox` (`Ui.PillboxOption` `Ui.PillboxOptionCreate` `Ui.PillboxOptionEmpty` `Ui.PillboxSearch` `Ui.PillboxTrigger` `Ui.PillboxInput`) `Ui.FileInput` `Ui.Checkbox` `Ui.CheckboxGroup` `Ui.CheckboxAll` `Ui.CheckboxIndicator` `Ui.RadioGroup` `Ui.Radio` `Ui.RadioIndicator` `Ui.Switch` `Ui.Slider` `Ui.SliderTick` `Ui.Rating` `Ui.Field` `Ui.Label` `Ui.Description` `Ui.Error` `Ui.Fieldset` `Ui.Legend` `Ui.Validator` `Ui.Otp` `Ui.OtpInput` `Ui.OtpSeparator` `Ui.OtpGroup` `Ui.Filter` `Ui.Calendar` `Ui.Editor` (`Ui.EditorToolbar` `Ui.EditorButton` `Ui.EditorContent` and a part per toolbar item — Flux's rich text editor on Tiptap; its engine is a separate script the build writes to `wwwroot/js`, fetched only by a page that mounts an editor) |
| Layout | `Ui.Separator` `Ui.Footer` `Ui.Hero` `Ui.Indicator` `Ui.Join` `Ui.Stack` `Ui.Mask` |
| Mockup | `Ui.MockupBrowser` `Ui.MockupCode` `Ui.MockupPhone` `Ui.MockupWindow` |
| Chrome | `Ui.Shell` `Ui.TopBar` `Ui.Brand` `Ui.Nav` `Ui.NavTab` `Ui.CrumbSwitcher` `Ui.TopLink` `Ui.Main` `Ui.Header` `Ui.MetricRow` `Ui.DetailList` `Ui.Code` |
| Support | `Ui.Icon` / `Ui.IconName` / `Ui.IconVariant` (all of Heroicons: outline, solid, mini, micro), `Ui.AppearanceScript` (dark mode), `Ui.Breakpoint`, `UiStyles`, `UiStylesheet` |

Requires .NET 10. Runs on both the ASP.NET host and browser-WebAssembly.

## Links

- [Repository](https://github.com/pal-tamas/rask)
- [Documentation](https://rask.sh/docs/guides/ui-kit)
- [Live components](https://rask.sh/docs/ui/actions)
