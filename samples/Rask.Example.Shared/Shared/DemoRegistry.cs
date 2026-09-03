using Rask.Core;

namespace Rask.Example.Shared;

// The set of live demos a guide can embed inline. A guide's markdown references a demo with an
// HTML-comment marker — `<!-- demo:key -->` — and the Markdown component (segmented render) looks
// the key up here and mounts the built component in place of the marker. The marker is invisible
// when the same docs/*.md renders on GitHub, so the guides stay dual-purpose (repo docs + on-site).
//
// Each entry is a *deferred* factory (Func<Component>), not a prebuilt instance, for two reasons:
//   1. CodeSample and the demo components can only be constructed inside a LiveRenderContext — the
//      generated factory throws otherwise — so construction must happen during render, not at
//      static-init time.
//   2. Every embed must mint its own fresh, independently-stateful instance.
// The factory bodies call the generated component factories (CodeSample(...), BindingTypedDemo(),
// …), available project-wide via the generator's `global using static …Generated`.
[global::Rask.Core.RaskMarkup]
public static partial class DemoRegistry
{
    private static readonly IReadOnlyDictionary<string, Func<Component>> Map =
        new Dictionary<string, Func<Component>>(StringComparer.Ordinal)
        {
            // --- Routing guide (code-only samples; the running showcase *is* the live demo) ---
            ["routing-nested-layout"] = () => CodeSample
                .Files(["RoutingLayoutDemo.cs"])
                .Notes("Component templates are joined to the parent's. An empty child template (\"\") means "
                + "\"default child for this layout\". This very showcase is built that way — every page "
                + "declares [ParentRoute(typeof(ShowcaseLayout))]."),
            ["routing-route-state"] = () => CodeSample
                .Files(["PathDisplay.cs"])
                .Notes("Subscribe to RouteState.Changed in OnMount and unsubscribe in OnUnmount. Useful for "
                + "components rendered above the Router (sidebars, breadcrumbs, the header path display) "
                + "that must refresh on every nav, including browser back/forward."),
            // Live: mutate the current URL's query through the scoped Navigator (its standalone example
            // page folded into docs/routing.md). Embed NavigatorDemo.cs as the teaching source.
            ["routing-navigator"] = () => CodeSample.Files(["NavigatorDemo.cs"]).Result(NavigatorQueryDemo),

            // --- Forms guide: two-way binding ---
            ["binding-manual"] = () => CodeSample
                .Files(["BindingManualDemo.cs"])
                .Notes("The low-level path: wire Value and the event handler yourself. Works for any input "
                + "type, but you parse and re-render manually.")
                .Result(BindingManualDemo),
            ["binding-typed"] = () => CodeSample
                .Files(["BindingTypedDemo.cs"])
                .Notes("Bind reads the expression — the property name becomes the input name, the property "
                + "type picks the input type, and string fields update on every keystroke. One call "
                + "replaces Value + OnInput + parsing.")
                .Result(BindingTypedDemo),
            ["binding-multi"] = () => CodeSample
                .Files(["BindingMultiDemo.cs"])
                .Notes("The same Bind helper picks the right input type from the property's CLR type and "
                + "wires immediate (string) or change-deferred (everything else) update timing.")
                .Result(BindingMultiDemo),
            ["binding-textarea"] = () => CodeSample
                .Files(["BindingTextareaDemo.cs"])
                .Notes("Textareas always stream — Textarea.Bound wires OnInputAsync for every keystroke so "
                + "the echo updates without blur or submit.")
                .Result(BindingTextareaDemo),

            // --- Forms guide: validation ---
            ["validation-fields"] = () => CodeSample
                .Files(["ValidationFieldsDemo.cs"])
                .Notes("Per-field DataAnnotations attributes with a ValidationMessage under each input — the "
                + "message appears once the field is touched and clears when it becomes valid.")
                .Result(ValidationFieldsDemo),
            ["validation-inline"] = () => CodeSample
                .Files(["InlineValidateDemo.cs"])
                .Notes("Inline Validate: on a field or the whole form — no extra package. Return the error "
                + "strings for the value; an empty result means valid.")
                .Result(InlineValidateDemo),
            ["validation-fluent"] = () => CodeSample
                .Files(["FluentValidationDemo.cs"])
                .Notes("An AbstractValidator<TModel>, discovered at compile time and run by the form with nothing "
                + "declared — the RuleFor chains drive the same ValidationMessage/ValidationSummary UI.")
                .Result(FluentValidationDemo),

            // --- Browser APIs guide: the typed wrappers over the platform, one live demo each (their
            //     standalone example pages folded into docs/browser-apis.md). ---
            ["browser-intersection"] = () => CodeSample.Files(["IntersectionObserverDemo.cs"]).Result(IntersectionObserverDemo),
            ["browser-resize"] = () => CodeSample.Files(["ResizeObserverDemo.cs"]).Result(ResizeObserverDemo),
            ["browser-mutation"] = () => CodeSample.Files(["MutationObserverDemo.cs"]).Result(MutationObserverDemo),
            ["browser-geolocation"] = () => CodeSample.Files(["GeolocationDemo.cs"]).Result(GeolocationDemo),
            ["browser-geolocation-watch"] = () => CodeSample.Files(["GeolocationWatchDemo.cs"]).Result(GeolocationWatchDemo),
            ["browser-device-sensors"] = () => CodeSample.Files(["DeviceSensorsDemo.cs"]).Result(DeviceSensorsDemo),
            ["browser-gamepad"] = () => CodeSample.Files(["GamepadDemo.cs"]).Result(GamepadDemo),
            ["browser-vibration"] = () => CodeSample.Files(["VibrationDemo.cs"]).Result(VibrationDemo),
            ["browser-navigator-info"] = () => CodeSample.Files(["NavigatorInfoDemo.cs"]).Result(NavigatorInfoDemo),
            ["browser-network"] = () => CodeSample.Files(["NetworkInfoDemo.cs"]).Result(NetworkInfoDemo),
            ["browser-battery"] = () => CodeSample.Files(["BatteryDemo.cs"]).Result(BatteryDemo),
            ["browser-screen"] = () => CodeSample.Files(["ScreenInfoDemo.cs"]).Result(ScreenInfoDemo),
            ["browser-visual-viewport"] = () => CodeSample.Files(["VisualViewportDemo.cs"]).Result(VisualViewportDemo),
            ["browser-media-query"] = () => CodeSample.Files(["MediaQueryDemo.cs"]).Result(MediaQueryDemo),
            ["browser-page-visibility"] = () => CodeSample.Files(["PageVisibilityDemo.cs"]).Result(PageVisibilityDemo),
            ["browser-performance"] = () => CodeSample.Files(["PerformanceDemo.cs"]).Result(PerformanceDemo),
            ["browser-permissions"] = () => CodeSample.Files(["PermissionsDemo.cs"]).Result(PermissionsDemo),
            ["browser-storage"] = () => CodeSample.Files(["StorageDemo.cs"]).Result(StorageDemo),
            ["browser-indexeddb"] = () => CodeSample.Files(["IndexedDbDemo.cs"]).Result(IndexedDbDemo),
            ["browser-cookies"] = () => CodeSample.Files(["CookiesDemo.cs"]).Result(CookiesDemo),
            ["browser-storage-estimate"] = () => CodeSample.Files(["StorageEstimateDemo.cs"]).Result(StorageEstimateDemo),
            ["browser-clipboard"] = () => CodeSample.Files(["ClipboardDemo.cs"]).Result(ClipboardDemo),
            ["browser-speech"] = () => CodeSample.Files(["SpeechDemo.cs"]).Result(SpeechDemo),
            ["browser-speech-recognition"] = () => CodeSample.Files(["SpeechRecognitionDemo.cs"]).Result(SpeechRecognitionDemo),
            ["browser-media-session"] = () => CodeSample.Files(["MediaSessionDemo.cs"]).Result(MediaSessionDemo),
            ["browser-crypto"] = () => CodeSample.Files(["CryptoDemo.cs"]).Result(CryptoDemo),
            ["browser-file-system"] = () => CodeSample.Files(["FileSystemAccessDemo.cs"]).Result(FileSystemAccessDemo),
            ["browser-opfs"] = () => CodeSample.Files(["OriginPrivateFileSystemDemo.cs"]).Result(OriginPrivateFileSystemDemo),
            ["browser-webauthn"] = () => CodeSample.Files(["WebAuthnDemo.cs"]).Result(WebAuthnDemo),
            ["browser-broadcast-channel"] = () => CodeSample.Files(["BroadcastChannelDemo.cs"]).Result(BroadcastChannelDemo),
            ["browser-web-locks"] = () => CodeSample.Files(["WebLocksDemo.cs"]).Result(WebLocksDemo),
            ["browser-signaling"] = () => CodeSample.Files(["SignalingDemo.cs"]).Result(SignalingDemo),
            ["browser-webrtc"] = () => CodeSample.Files(["WebRtcDemo.cs"]).Result(WebRtcDemo),
            ["browser-notifications"] = () => CodeSample.Files(["NotificationsDemo.cs"]).Result(NotificationsDemo),
            ["browser-share"] = () => CodeSample
                .Files(["ShareDemo.cs"])
                .Notes("Shareable (Rask.Core) is headless — you render the trigger element, it hands you the "
                + "data-rask-share attribute to spread onto it. The shared client fires navigator.share inside "
                + "the click gesture, so the transient user activation survives even on the Server transport "
                + "(an imperative round-trip would lose it), and it works on every host. For a code-driven "
                + "share on the WASM host, inject IShare from Rask.Wasm.Browser.")
                .Result(ShareDemo),
            ["browser-gesture-bridge"] = () => CodeSample
                .Files(["GestureBridgeDemo.cs"])
                .Notes("The GestureTrigger family (Rask.Core) is headless like Shareable: each trigger hands your "
                + "element a data-rask-gesture attribute and the shared client runs the activation-gated API "
                + "inside the click gesture. That makes normally-WASM-only APIs reachable on every host, the "
                + "Server included — where the imperative IFullscreen / IEyeDropper / … services can't be "
                + "injected, because a round-trip would lose the transient user activation. Six typed triggers "
                + "ship: FullscreenTrigger, ScreenOrientationTrigger, EyeDropperTrigger, InstallTrigger, "
                + "MediaCaptureTrigger, and PictureInPictureTrigger (the last two target a <video> via its "
                + "ElementRef). Capabilities that return a value (the eyedropper's hex, the install outcome) "
                + "post it back to the OnColor / OnResult / OnOutcome callback.")
                .Result(GestureBridgeDemo),

            // --- Forms guide: the remaining two-way-binding variants (their standalone /binding page
            //     folded into docs/forms.md). ---
            ["binding-nullable"] = () => CodeSample.Files(["BindingNullableDemo.cs"]).Result(BindingNullableDemo),
            ["binding-clear-default"] = () => CodeSample.Files(["BindingClearDefaultDemo.cs"]).Result(BindingClearDefaultDemo),
            ["binding-afterbind"] = () => CodeSample.Files(["BindingAfterBindDemo.cs"]).Result(BindingAfterBindDemo),
            ["binding-afterbind-async"] = () => CodeSample.Files(["BindingAfterBindAsyncDemo.cs"]).Result(BindingAfterBindAsyncDemo),

            // --- Forms guide: the form-controls matrix (each control controlled + bound). ---
            ["form-controls-input"] = () => CodeSample.Files(["FormControlsInputDemo.cs"]).Result(FormControlsInputDemo),
            ["form-controls-textarea"] = () => CodeSample.Files(["FormControlsTextareaDemo.cs"]).Result(FormControlsTextareaDemo),
            ["form-controls-select"] = () => CodeSample.Files(["FormControlsSelectDemo.cs"]).Result(FormControlsSelectDemo),
            ["floating-labels"] = () => CodeSample.Files(["FloatingLabelsDemo.cs"]).Result(FloatingLabelsDemo),

            // --- Forms guide: the remaining validation demos (their standalone /validation page folded in). ---
            ["validation-summary"] = () => CodeSample.Files(["ValidationSummaryDemo.cs"]).Result(ValidationSummaryDemo),
            ["validation-inline-async"] = () => CodeSample.Files(["InlineAsyncValidateDemo.cs"]).Result(InlineAsyncValidateDemo),
            ["validation-custom-attribute"] = () => CodeSample.Files(["CustomAttributeDemo.cs"]).Result(CustomAttributeDemo),
            ["validation-validatable-object"] = () => CodeSample.Files(["ValidatableObjectDemo.cs"]).Result(ValidatableObjectDemo),
            ["validation-fluent-async"] = () => CodeSample.Files(["FluentValidationAsyncDemo.cs"]).Result(FluentValidationAsyncDemo),
            ["validation-async"] = () => CodeSample.Files(["AsyncValidationDemo.cs"]).Result(AsyncValidationDemo),
            ["validation-programmatic"] = () => CodeSample.Files(["ProgrammaticValidateDemo.cs"]).Result(ProgrammaticValidateDemo),
            ["validation-first-error-wins"] = () => CodeSample.Files(["FirstErrorWinsDemo.cs"]).Result(FirstErrorWinsDemo),
            ["validation-cross-field"] = () => CodeSample.Files(["CrossFieldSummaryDemo.cs"]).Result(CrossFieldSummaryDemo),
            ["validation-nested-async"] = () => CodeSample.Files(["NestedAsyncWithLiveTotalsDemo.cs"]).Result(NestedAsyncWithLiveTotalsDemo),

            // --- Forms guide: nested / complex models (their standalone /nested-forms page folded in). ---
            ["nested-subobject"] = () => CodeSample.Files(["NestedSubObjectDemo.cs"]).Result(NestedSubObjectDemo),
            ["nested-list-foreach"] = () => CodeSample.Files(["NestedListForeachDemo.cs"]).Result(NestedListForeachDemo),
            ["nested-list-indexer"] = () => CodeSample.Files(["NestedListIndexerDemo.cs"]).Result(NestedListIndexerDemo),
            ["nested-fluent"] = () => CodeSample.Files(["NestedFluentValidationDemo.cs"]).Result(NestedFluentValidationDemo),

            // --- Forms guide: radio/checkbox groups + multi-select example components. ---

            // --- Composition guide: context, callbacks, virtualize, keyed lists, drag & drop, error
            //     boundaries (their standalone example pages folded into docs/composition.md). ---
            // The three ways to author a reusable unit — static method, stateless component, stateful
            // component — shown side by side; the three code tabs are the tiers themselves.
            ["component-tiers"] = () => CodeSample
                .Files(["TierStaticHelperDemo.cs", "TierStatelessGreetingDemo.cs", "TierStatefulCounterDemo.cs"])
                .Result(ComponentTiersDemo),
            ["context-theme"] = () => CodeSample.Files(["ContextThemeDemo.cs"]).Result(ContextThemeDemo),
            ["callback-rating"] = () => CodeSample.Files(["CallbackRatingDemo.cs"]).Result(CallbackRatingDemo),
            ["virtualize-items"] = () => CodeSample.Files(["VirtualizeItemsDemo.cs"]).Result(VirtualizeItemsDemo),
            ["virtualize-provider"] = () => CodeSample.Files(["VirtualizeProviderDemo.cs"]).Result(VirtualizeProviderDemo),
            ["keyed-lists-reorder"] = () => CodeSample.Files(["KeyedListsReorderDemo.cs"]).Result(KeyedListsReorderDemo),
            ["master-detail"] = () => CodeSample.Files(["MasterDetailDemo.cs"]).Result(MasterDetailDemo),
            ["drag-drop-sortable"] = () => CodeSample.Files(["DragDropSortableDemo.cs"]).Result(DragDropSortableDemo),
            ["drag-drop-kanban"] = () => CodeSample.Files(["DragDropKanbanDemo.cs"]).Result(DragDropKanbanDemo),
            ["boom-handler"] = () => CodeSample.Files(["BoomHandlerDemo.cs"]).Result(BoomHandlerDemo),
            ["boom-render"] = () => CodeSample.Files(["BoomRenderDemo.cs"]).Result(BoomRenderDemo),
            ["boom-nested"] = () => CodeSample.Files(["BoomNestedDemo.cs"]).Result(BoomNestedDemo),

            // --- Lifecycle guide: hooks, mount/unmount cycle, disposal, cancellation, background
            //     service (their standalone example pages folded into docs/lifecycle.md). The demos
            //     embed the probe source — the teaching artifact — while Result mounts the live widget. ---
            ["lifecycle-hooks"] = () => CodeSample.Files(["LifecycleProbe.cs"]).Result(LifecycleProbe),
            ["lifecycle-cycle"] = () => CodeSample.Files(["LifecycleCycleProbe.cs"]).Result(LifecycleCycleDemo),
            // Live ticker (its standalone /realtime/{Symbol} page folded in): a poll loop in OnMountAsync
            // + a symbol switch that fires OnPropsChanged, drawing a zero-JS server-rendered SVG chart.
            ["lifecycle-ticker"] = () => CodeSample
                .Files(["LiveTicker.cs"])
                .Notes("OnMountAsync runs a long-lived poll loop; every await uses ConfigureAwait(false) so it "
                + "calls StateHasChanged() once per real data change (one render per tick). Switching the "
                + "symbol fires OnPropsChanged/OnPropsChangedAsync, which clears the buffer and wakes the loop "
                + "so the new asset polls immediately; CancellationToken cancels the loop on unmount. The chart "
                + "is a server-rendered SVG (Sparkline) emitted straight from Render() — no canvas, no JS. The "
                + "feed is a local random-walk (offline-safe); swapping in a real HTTP source is a one-line "
                + "change in PollOnceAsync.")
                .Result(LiveTickerDemo),
            ["disposal-sync"] = () => CodeSample.Files(["DisposableTimerProbe.cs"]).Result(DisposalSyncDemo),
            ["disposal-async"] = () => CodeSample.Files(["DisposableAsyncProbe.cs"]).Result(DisposalAsyncDemo),
            ["disposal-unmount"] = () => CodeSample.Files(["UnmountTimerProbe.cs"]).Result(DisposalUnmountDemo),
            ["cancellation"] = () => CodeSample.Files(["CancellationProbe.cs"]).Result(CancellationDemo),
            ["background-metrics"] = () => CodeSample
                .Files(["MetricsFeed.cs", "MetricsGauge.cs", "MetricsChart.cs"])
                .Result(BackgroundMetricsDemo),

            // --- JS-interop guide: element refs, scoped CSS, scoped JS / IJSRuntime, and the asset-
            //     loading story (their standalone example pages folded into docs/js-interop.md). ---
            ["js-interop-elementref"] = () => CodeSample
                .Files(["ElementRefDemo.cs", "ElementRefDemo.ts"])
                .Result(ElementRefDemo),
            ["js-interop-scoped-css"] = () => CodeSample
                .Files(["ScopedRed.cs", "ScopedBlue.cs", "ScopedRed.css", "ScopedBlue.css"])
                .Result(Div.Class("flex gap-2 flex-col")[ScopedRed, ScopedBlue]),
            ["js-interop-jsruntime"] = () => CodeSample.Files(["JsRuntimeDemo.cs"]).Result(JsRuntimeDemo),
            ["js-interop-thirdparty"] = () => CodeSample
                .Files(["GanttDemo.cs", "Gantt.cs", "Gantt.ts"])
                .Notes("A wrapper around frappe-gantt (MIT, vendored under wwwroot/lib). The library owns "
                + "every node inside the host div, so the component renders that div as a leaf and lets "
                + "Gantt.js fill it — props in, C# delegates out. Drag or resize a bar and the table below "
                + "updates: that path is browser → [JSInvokable] → C# state → live re-render. Two things "
                + "worth copying: the chart's nodes are tagged data-rask-managed, without which the first "
                + "full-HTML frame would morph them away seconds after they draw, and the library's "
                + "stylesheet, injected into <head> at runtime, is preserved with no code at all.")
                .Result(GanttDemo),

            // --- CQRS guide: one vertical slice (query + result-command + notification + a pipeline
            //     behaviour), dispatched reflection-free by the Rask.Cqrs source generator. ---
            ["cqrs-counter"] = () => CodeSample
                .Files(["CqrsCounterDemo.cs", "CounterSlice.cs"])
                .Notes("One slice, all four message shapes: a query (GetCounterState), a command that returns "
                + "a value (IncrementCounter), a notification the command publishes (CounterIncremented), "
                + "and a pipeline behaviour (DispatchLogBehavior) that wraps every dispatch — the "
                + "generator wires them with no runtime reflection.")
                .Result(CqrsCounterDemo),
            ["asset-basic-css"] = () => CodeSample
                .Files(["BasicScopedCss.cs", "BasicScopedCss.css"])
                .Result(BasicScopedCss),
            ["asset-js-only"] = () => CodeSample.Files(["JsOnlyDemo.cs", "JsOnlyDemo.ts"]).Result(JsOnlyDemo),
            ["asset-twin-bundle"] = () => CodeSample
                .Files(["TwinA.cs", "TwinA.css"])
                .Result(Div.Class("flex gap-2 flex-wrap items-center")[TwinA, TwinB]),
            ["asset-lazy-mount"] = () => CodeSample
                .Files(["LazyMount.cs", "LazyChild.cs", "LazyChild.css"])
                .Result(LazyMount),

            // --- Localization guide ---
            ["localization-formats"] = () => CodeSample.Files(["CultureFormatsDemo.cs"])
                .Title("The same values, four languages")
                .Notes("Fixed sample values and an explicit CultureInfo per call — a demo that read the "
                       + "ambient culture would reformat every other demo on this page.")
                .Result(CultureFormatsDemo),

            // --- Elements & the DSL guide: primitives, tag factories, universal props, SVG, and the
            //     HTML element catalog (their standalone example pages folded into docs/elements.md).
            //     Each demo already lived in its own *Demo.cs. ---
            ["primitives-text"] = () => CodeSample.Files(["PrimitivesTextDemo.cs"]).Result(PrimitivesTextDemo),
            ["primitives-raw"] = () => CodeSample.Files(["PrimitivesRawDemo.cs"]).Result(PrimitivesRawDemo),
            ["primitives-fragment"] = () => CodeSample.Files(["PrimitivesFragmentDemo.cs"]).Result(PrimitivesFragmentDemo),
            ["primitives-doctype"] = () => CodeSample.Files(["PrimitivesDoctypeDemo.cs"]).Result(PrimitivesDoctypeDemo),
            ["primitives-children"] = () => CodeSample.Files(["PrimitivesChildrenDemo.cs"]).Result(PrimitivesChildrenDemo),
            ["tags-text"] = () => CodeSample.Files(["TagsTextDemo.cs"]).Result(TagsTextDemo),
            ["tags-form"] = () => CodeSample.Files(["TagsFormDemo.cs"]).Result(TagsFormDemo),
            ["tags-table"] = () => CodeSample.Files(["TagsTableDemo.cs"]).Result(TagsTableDemo),
            ["tags-media"] = () => CodeSample.Files(["TagsMediaDemo.cs"]).Result(TagsMediaDemo),
            ["tags-void"] = () => CodeSample.Files(["TagsVoidDemo.cs"]).Result(TagsVoidDemo),
            ["props-id-class-style"] = () => CodeSample.Files(["PropsIdClassStyleDemo.cs"]).Result(PropsIdClassStyleDemo),
            ["props-data"] = () => CodeSample.Files(["PropsDataDemo.cs"]).Result(PropsDataDemo),
            ["props-aria"] = () => CodeSample.Files(["PropsAriaDemo.cs"]).Result(PropsAriaDemo),
            ["props-attributes"] = () => CodeSample.Files(["PropsAttributesDemo.cs"]).Result(PropsAttributesDemo),
            ["props-command"] = () => CodeSample.Files(["PropsCommandDemo.cs"]).Result(PropsCommandDemo),
            ["props-attribute-order"] = () => CodeSample.Files(["PropsAttributeOrderDemo.cs"]).Result(PropsAttributeOrderDemo),
            ["svg-shapes"] = () => CodeSample.Files(["SvgShapesDemo.cs"]).Result(SvgShapesDemo),
            ["svg-gradient"] = () => CodeSample.Files(["SvgGradientDemo.cs"]).Result(SvgGradientDemo),
            ["svg-clickable"] = () => CodeSample.Files(["SvgClickableDemo.cs"]).Result(SvgClickableDemo),
            ["svg-text"] = () => CodeSample.Files(["SvgTextDemo.cs"]).Result(SvgTextDemo),
            ["elements-text"] = () => CodeSample.Files(["ElementsTextDemo.cs"]).Result(ElementsTextDemo),
            ["elements-grouping"] = () => CodeSample.Files(["ElementsGroupingDemo.cs"]).Result(ElementsGroupingDemo),
            ["elements-sections"] = () => CodeSample.Files(["ElementsSectionsDemo.cs"]).Result(ElementsSectionsDemo),
            ["elements-forms"] = () => CodeSample.Files(["ElementsFormsDemo.cs"]).Result(ElementsFormsDemo),
            ["elements-tables"] = () => CodeSample.Files(["ElementsTablesDemo.cs"]).Result(ElementsTablesDemo),
            ["elements-media"] = () => CodeSample.Files(["ElementsMediaDemo.cs"]).Result(ElementsMediaDemo),
            ["elements-interactive"] = () => CodeSample.Files(["ElementsInteractiveDemo.cs"]).Result(ElementsInteractiveDemo),
            ["elements-metadata"] = () => CodeSample.Files(["ElementsMetadataDemo.cs"]).Result(ElementsMetadataDemo),

            // --- HTTP & files guide: the HttpClient+DI, file-upload and file-download example pages
            // folded into docs/http-and-files.md as inline live demos. ---
            ["data-http-register"] = () => CodeSample
                .Files(["HttpRegisterDemo.cs"])
                .Notes("Relative URLs require BaseAddress. WasmHostBuilder.BaseAddress is the app root (and carries "
                + "any sub-path) — read it lazily inside the factory so it fires after the JS module imports.")
                .Result(HttpRegisterDemo),
            ["data-http-fetch"] = () => CodeSample
                .Files(["HttpFetchDemo.cs"])
                .Notes("OnMountAsync runs once on first render. The framework's async lifecycle handler triggers a "
                + "re-render when the awaited task completes. Component.CancellationToken cancels on unmount — "
                + "navigate away mid-fetch and the in-flight request aborts.")
                .Result(HttpFetchDemo),
            ["data-upload"] = () => CodeSample
                .Files(["UploadDemo.cs"])
                .Notes("The handler runs once per change event. RaskFile is only valid while the handler is on the "
                + "stack — read whatever you need (bytes, metadata) before returning. The same component code "
                + "runs unchanged on both hosts.")
                .Result(UploadDemo),
            ["data-download"] = () => CodeSample
                .Files(["DownloadDemo.cs"])
                .Notes("Navigator.Download must be called from an event handler — outside that scope it throws, "
                + "because there's no live render round-trip to attach the download to. The handler can do "
                + "other state changes too (here, bump a counter); both ship in the same render.")
                .Result(DownloadDemo),

            // --- Components-group example pages folded into their existing guides (part 1). ---
            // Events → composition.md (the GlobalEventHandlers surface).
            ["events"] = () => CodeSample
                .Files(["EventsDemo.cs"])
                .Notes("Every handler just mutates a field; the framework re-renders the component that owns the "
                + "callback, so the readouts update on their own. MouseEventArgs carries button/coords/modifiers, "
                + "WheelEventArgs adds deltas, ClipboardEventArgs the pasted text. Wiring both OnX and OnXAsync "
                + "for one event is a compile error (RASK027) — pick one.")
                .Result(EventsDemo),
            ["events-click"] = () => CodeSample.Files(["EventsClickDemo.cs"]).Result(EventsClickDemo),
            ["events-input"] = () => CodeSample.Files(["EventsInputDemo.cs"]).Result(EventsInputDemo),
            ["events-select"] = () => CodeSample.Files(["EventsSelectDemo.cs"]).Result(EventsSelectDemo),
            ["events-form"] = () => CodeSample
                .Files(["EventsFormDemo.cs"])
                .Notes("OnSubmit receives a FormData object collected from all named form fields.")
                .Result(EventsFormDemo),
            // User & auth → authentication.md (imperative gate + declarative Authorize).
            ["auth-user-gate"] = () => CodeSample
                .Files(["UserGateDemo.cs"])
                .Notes("The principal resolves from the IUserProvider in scope. A component that gates on the user "
                + "subscribes to the provider's Changed event — the same pattern sidebars use for RouteState — "
                + "so it re-renders when the principal changes.")
                .Result(UserGateDemo),
            ["auth-authorize"] = () => CodeSample
                .Files(["AuthorizeDemo.cs"])
                .Notes("Authorize picks the Authorized, NotAuthorized, or Authorizing slot off the same IUserProvider. "
                + "Roles and the authenticated check are synchronous (no flicker); Policy resolves in the "
                + "background. For whole-page gating use [Authorize] on the page instead.")
                .Result(AuthorizeDemo),

            // --- User components (factory generation) → getting-started.md §6 (its /components page folded in). ---
            ["components-greeting"] = () => CodeSample
                .Files(["ComponentsGreetingDemo.cs"])
                .Notes("Non-nullable property without an initializer → required factory parameter. Nullable property "
                + "→ optional with default null. Property with an initializer → excluded from the factory.")
                .Result(ComponentsGreetingDemo),
            ["components-di"] = () => CodeSample
                .Files(["ComponentsDiDemo.cs"])
                .Notes("Inject services (HttpClient/Navigator/RouteState) through the constructor, never as a public "
                + "settable property — that would become a required factory parameter, and `required` on a "
                + "property with a DI-only constructor is RASK002. Constructor params resolve from DI via "
                + "ActivatorUtilities; only public settable properties feed the generated factory."),
            ["components-skipfactory"] = () => CodeSample
                .Files(["ComponentsSkipFactoryDemo.cs"])
                .Notes("[SkipFactory] keeps a property settable in code while removing it from the generated factory "
                + "signature. The counter below started at 7 — click it and the state persists across re-renders.")
                .Result(ComponentsSkipFactoryDemo),
        };

    // Whether a demo key is registered (guides referencing an unknown key render a visible warning
    // and fail the registry-integrity unit test).
    public static bool Contains(string key) => Map.ContainsKey(key);

    // Builds a fresh instance of the demo. Must be called during render (LiveRenderContext ambient).
    public static Component Build(string key) => Map[key]();

    // All registered keys — used by the integrity test to assert no demo is orphaned.
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Map.Keys;
}
