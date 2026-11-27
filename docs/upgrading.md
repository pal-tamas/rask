# Upgrading Rask

> **Moving an app's model onto aggregates instead?** That is [Upgrading to the domain way](upgrading-to-domain.md).

Rask is before 1.0, so a release can remove an API outright rather than keep a deprecated twin of it. This page
lists what a released version took away and what replaces it, so an app written against an older release — or an
answer written from one — finds its way to the current API. The guides describe only the current API; the
[CHANGELOG](../CHANGELOG.md) has the reasons and the full detail of every change.

Everything below was gone by **0.24.0**. A removal still on `main` and not yet in a release is noted in the guide it
belongs to and in the CHANGELOG's *Unreleased* section until the next release ships it.

## The CLI

| Removed | Use instead |
| --- | --- |
| `rask new --auth` | Nothing: accounts are a battery like the rest, on by default. `rask new` refuses the flag by name; an app declines accounts with `app.Configure(c => c.Auth.Off())`. |
| `--data`, `--jobs`, `--all-batteries` and the other positive battery flags | Every battery is included; drop one with `--no-<battery>`. The old flags fail with a message naming the replacement instead of being accepted and disregarded. |
| `OptInFlags` (#854) | Languages are configured in appsettings.json (`Rask:Cultures`, the first entry the default), never on the command line. |

## Rendering and navigation

| Removed | Use instead |
| --- | --- |
| `NavigationQuiescenceTimeout` | Nothing: a page reached in place is sent at once and what its `OnMount`/`OnUpdated` read follows in the next frame. An appsettings key of that name is ignored. |

## CQRS and events

| Removed | Use instead |
| --- | --- |
| `Notify` | Inject `IDispatcher` where one is to hand. |
| `IBroadcast` / `Topic<T>` | An event is the message: `IDispatcher.Publish(event)` runs its `IEventHandler`s and hands it to every open subscription (`QueryClient.Subscribe<T>()`). See [Subscriptions](subscriptions.md). |
| `IOutboxEvent`, `RaskDataOptions.DispatchDomainEventsInProcess` | `IEventHandler<T>` runs in memory after the commit; `IDurableHandler<T>` gets its own outbox row in the same transaction. See [the outbox](outbox.md). |

## Data

| Removed | Use instead |
| --- | --- |
| `ITimestamped`, `IVersioned`, `ISoftDeletable`, `IValueObject`, `[SkipModel]` | The traits a table carries are marker interfaces beside the aggregate's base (`ISoftDeletes`, `IHasForm`, …), read like a sentence. See [Domain modelling](domain.md). |

## The UI kit

Each `Ui.*` component below was rebuilt as Flux UI's own, and nothing beyond Flux's surface stayed.

| Component | Removed | Use instead |
| --- | --- | --- |
| Toast | the page-owned `Ui.Toast.Message(…).OnDismiss(…)`, `Ui.Toaster` | the frame's `Ui.Toast`, mounted once by `Ui.AppFrame` |
| Checkbox, radio, switch | `Tone`/`Size`, `Options(...)`, `Layout(Ui.ChoiceLayout.X)`, `CheckAll()` | option children; `Ui.CheckboxAll` inside `Ui.CheckboxGroup` |
| Slider | `Ui.Range` | `Ui.Slider.Bind(() => m.Amount).Min(0).Max(100).Step(10)` |
| OTP | `Joined`, `Tone`, `Size`, `Hint`, `Description`, `Badge`, `Invalid`, `AccessibleLabel`, required `Length` | `Ui.OtpGroup` for `Joined`, `DescriptionTrailing` for `Hint`; the bound member's message marks the cells |
| Input, textarea | `Ui.Search`, floating labels/`Floating`, `Hint`, `Tone`/`Error("…")`, `AccessibleLabel`, `ShowValidating`, `AutoSize`, `Ui.Resize`, the old `Size`/`Variant` | `Ui.Input.Icon(Ui.IconName.MagnifyingGlass)`, `Description`, `Invalid()` + `Ui.Error.Message("…")`, `Rows(UiTextareaRows.Auto)`, `Resize` |
| Select | `.Options(list)`, `Ui.MultiSelect`, `Native()`, `OptionTemplate`, `OptionGroup`, `OptionDisabled`, `Filter((v, text) => …)`/`OnSearch`/`Loading`, `EmptyText`/`LoadingText`, `SelectAll()`, `Chips(n)`, `Floating`, `Tone`, the old `Variant`/`Size`, `Hint`, `AccessibleLabel` | option children, `.Listbox`, the option's children/`Icon`/`Avatar`/`Description`, `Ui.SelectGroup`, `Ui.SelectOption.Disabled()`, `Filter(false)`, `Empty`, `Description`, `Label` |
| Calendar, date picker | the `Label`/`Month`/`OnMonth`/`FirstDay`/`Hint` steps, `Ui.DatePicker.Values` | the mode step that opens the control: `Ui.Calendar.Bind(() => m.Day)` and its range and multiple forms |
| File input | `Ui.FileInput` and its `Dropzone()` | `Ui.FileUpload` with `Ui.FileUploadDropzone`; a plain file field is `Ui.Input.Of<string>().Type(InputType.File).OnFiles(…)` |
| App frame | `Ui.Shell`, `Ui.TopBar`, `Ui.SidebarFooter`, the page-heading `Ui.Header.Title(...)` | `Ui.AppFrame` with `Ui.SidebarNav` |
| Separator | `Ui.Divider` | `Ui.Separator` |
| Fieldset, label | `Ui.Fieldset.Text`/`Help`, the old `Ui.Label.Text` | `Ui.Fieldset.Legend("…").Description("…")` |
| Accordion | `Ui.Collapse`, `Ui.AccordionSection`, `Ui.Marker` | `Ui.Accordion[Ui.AccordionItem.Heading("…")["…"]]` |
| Icon | the kit's names `Close`/`Search`/`Warning`/`Gear` | Heroicons' own names: `XMark`, `MagnifyingGlass`, `ExclamationTriangle`, and a cog is `Cog6Tooth` |
| Pagination | `Pages`/`Current` | `Ui.Table.Paginate(…)` |
| Timeline | `Ui.Timeline.Vertical` | nothing: vertical is the default |
| Swap | `Ui.Swap` | `Ui.Toggle.OnIcon(…).OffIcon(…)` |

The [UI kit guide](ui-kit.md) and each component's page on [rask.sh/ui](https://rask.sh/ui/) show the current API.
