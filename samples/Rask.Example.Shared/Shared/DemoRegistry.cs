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
public static class DemoRegistry
{
    private static readonly IReadOnlyDictionary<string, Func<Component>> Map =
        new Dictionary<string, Func<Component>>(StringComparer.Ordinal)
        {
            // --- Routing guide (code-only samples; the running showcase *is* the live demo) ---
            ["routing-nested-layout"] = () => CodeSample(
                ["RoutingLayoutDemo.cs"],
                Notes:
                "Child templates are joined to the parent's. An empty child template (\"\") means "
                + "\"default child for this layout\". This very showcase is built that way — every page "
                + "declares [ParentRoute(typeof(ShowcaseLayout))]."),
            ["routing-route-state"] = () => CodeSample(
                ["PathDisplay.cs"],
                Notes:
                "Subscribe to RouteState.Changed in OnMount and unsubscribe in OnUnmount. Useful for "
                + "components rendered above the Router (sidebars, breadcrumbs, the header path display) "
                + "that must refresh on every nav, including browser back/forward."),
            // Live: mutate the current URL's query through the scoped Navigator (its standalone example
            // page folded into docs/routing.md). Embed NavigatorDemo.cs as the teaching source.
            ["routing-navigator"] = () => CodeSample(["NavigatorDemo.cs"], Result: NavigatorQueryDemo()),

            // --- Forms guide: two-way binding ---
            ["binding-manual"] = () => CodeSample(
                ["BindingManualDemo.cs"],
                Notes:
                "The low-level path: wire Value and the event handler yourself. Works for any input "
                + "type, but you parse and re-render manually.",
                Result: BindingManualDemo()),
            ["binding-typed"] = () => CodeSample(
                ["BindingTypedDemo.cs"],
                Notes:
                "Bind reads the expression — the property name becomes the input name, the property "
                + "type picks the input type, and string fields update on every keystroke. One call "
                + "replaces Value + OnInput + parsing.",
                Result: BindingTypedDemo()),
            ["binding-multi"] = () => CodeSample(
                ["BindingMultiDemo.cs"],
                Notes:
                "The same Bind helper picks the right input type from the property's CLR type and "
                + "wires immediate (string) or change-deferred (everything else) update timing.",
                Result: BindingMultiDemo()),
            ["binding-textarea"] = () => CodeSample(
                ["BindingTextareaDemo.cs"],
                Notes:
                "Textareas always stream — Textarea.Bound wires OnInputAsync for every keystroke so "
                + "the echo updates without blur or submit.",
                Result: BindingTextareaDemo()),

            // --- Forms guide: validation ---
            ["validation-fields"] = () => CodeSample(
                ["ValidationFieldsDemo.cs"],
                Notes:
                "Per-field DataAnnotations attributes with a ValidationMessage under each input — the "
                + "message appears once the field is touched and clears when it becomes valid.",
                Result: ValidationFieldsDemo()),
            ["validation-inline"] = () => CodeSample(
                ["InlineValidateDemo.cs"],
                Notes:
                "Inline Validate: on a field or the whole form — no extra package. Return the error "
                + "strings for the value; an empty result means valid.",
                Result: InlineValidateDemo()),
            ["validation-fluent"] = () => CodeSample(
                ["FluentValidationDemo.cs"],
                Notes:
                "An AbstractValidator<TModel> wired to the form via the Rask.Validation.FluentValidation "
                + "package — the RuleFor chains drive the same ValidationMessage/ValidationSummary UI.",
                Result: FluentValidationDemo()),

            // --- Browser APIs guide: the typed wrappers over the platform, one live demo each (their
            //     standalone example pages folded into docs/browser-apis.md). ---
            ["browser-intersection"] = () => CodeSample(["IntersectionObserverDemo.cs"], Result: IntersectionObserverDemo()),
            ["browser-resize"] = () => CodeSample(["ResizeObserverDemo.cs"], Result: ResizeObserverDemo()),
            ["browser-mutation"] = () => CodeSample(["MutationObserverDemo.cs"], Result: MutationObserverDemo()),
            ["browser-geolocation"] = () => CodeSample(["GeolocationDemo.cs"], Result: GeolocationDemo()),
            ["browser-geolocation-watch"] = () => CodeSample(["GeolocationWatchDemo.cs"], Result: GeolocationWatchDemo()),
            ["browser-device-sensors"] = () => CodeSample(["DeviceSensorsDemo.cs"], Result: DeviceSensorsDemo()),
            ["browser-gamepad"] = () => CodeSample(["GamepadDemo.cs"], Result: GamepadDemo()),
            ["browser-vibration"] = () => CodeSample(["VibrationDemo.cs"], Result: VibrationDemo()),
            ["browser-navigator-info"] = () => CodeSample(["NavigatorInfoDemo.cs"], Result: NavigatorInfoDemo()),
            ["browser-network"] = () => CodeSample(["NetworkInfoDemo.cs"], Result: NetworkInfoDemo()),
            ["browser-screen"] = () => CodeSample(["ScreenInfoDemo.cs"], Result: ScreenInfoDemo()),
            ["browser-visual-viewport"] = () => CodeSample(["VisualViewportDemo.cs"], Result: VisualViewportDemo()),
            ["browser-media-query"] = () => CodeSample(["MediaQueryDemo.cs"], Result: MediaQueryDemo()),
            ["browser-page-visibility"] = () => CodeSample(["PageVisibilityDemo.cs"], Result: PageVisibilityDemo()),
            ["browser-performance"] = () => CodeSample(["PerformanceDemo.cs"], Result: PerformanceDemo()),
            ["browser-permissions"] = () => CodeSample(["PermissionsDemo.cs"], Result: PermissionsDemo()),
            ["browser-storage"] = () => CodeSample(["StorageDemo.cs"], Result: StorageDemo()),
            ["browser-indexeddb"] = () => CodeSample(["IndexedDbDemo.cs"], Result: IndexedDbDemo()),
            ["browser-cookies"] = () => CodeSample(["CookiesDemo.cs"], Result: CookiesDemo()),
            ["browser-storage-estimate"] = () => CodeSample(["StorageEstimateDemo.cs"], Result: StorageEstimateDemo()),
            ["browser-clipboard"] = () => CodeSample(["ClipboardDemo.cs"], Result: ClipboardDemo()),
            ["browser-speech"] = () => CodeSample(["SpeechDemo.cs"], Result: SpeechDemo()),
            ["browser-media-session"] = () => CodeSample(["MediaSessionDemo.cs"], Result: MediaSessionDemo()),
            ["browser-crypto"] = () => CodeSample(["CryptoDemo.cs"], Result: CryptoDemo()),
            ["browser-file-system"] = () => CodeSample(["FileSystemAccessDemo.cs"], Result: FileSystemAccessDemo()),
            ["browser-webauthn"] = () => CodeSample(["WebAuthnDemo.cs"], Result: WebAuthnDemo()),
            ["browser-broadcast-channel"] = () => CodeSample(["BroadcastChannelDemo.cs"], Result: BroadcastChannelDemo()),

            // --- Forms guide: the remaining two-way-binding variants (their standalone /binding page
            //     folded into docs/forms.md). ---
            ["binding-nullable"] = () => CodeSample(["BindingNullableDemo.cs"], Result: BindingNullableDemo()),
            ["binding-clear-default"] = () => CodeSample(["BindingClearDefaultDemo.cs"], Result: BindingClearDefaultDemo()),
            ["binding-afterbind"] = () => CodeSample(["BindingAfterBindDemo.cs"], Result: BindingAfterBindDemo()),
            ["binding-afterbind-async"] = () => CodeSample(["BindingAfterBindAsyncDemo.cs"], Result: BindingAfterBindAsyncDemo()),

            // --- Forms guide: the form-controls matrix (each control controlled + bound). ---
            ["form-controls-input"] = () => CodeSample(["FormControlsInputDemo.cs"], Result: FormControlsInputDemo()),
            ["form-controls-textarea"] = () => CodeSample(["FormControlsTextareaDemo.cs"], Result: FormControlsTextareaDemo()),
            ["form-controls-select"] = () => CodeSample(["FormControlsSelectDemo.cs"], Result: FormControlsSelectDemo()),
            ["form-controls-radio"] = () => CodeSample(["FormControlsRadioDemo.cs"], Result: FormControlsRadioDemo()),
            ["form-controls-checkbox"] = () => CodeSample(["FormControlsCheckboxDemo.cs"], Result: FormControlsCheckboxDemo()),
            ["form-controls-multiselect"] = () => CodeSample(["FormControlsMultiSelectDemo.cs"], Result: FormControlsMultiSelectDemo()),
            ["floating-labels"] = () => CodeSample(["FloatingLabelsDemo.cs"], Result: FloatingLabelsDemo()),

            // --- Forms guide: the remaining validation demos (their standalone /validation page folded in). ---
            ["validation-summary"] = () => CodeSample(["ValidationSummaryDemo.cs"], Result: ValidationSummaryDemo()),
            ["validation-inline-async"] = () => CodeSample(["InlineAsyncValidateDemo.cs"], Result: InlineAsyncValidateDemo()),
            ["validation-custom-attribute"] = () => CodeSample(["CustomAttributeDemo.cs"], Result: CustomAttributeDemo()),
            ["validation-validatable-object"] = () => CodeSample(["ValidatableObjectDemo.cs"], Result: ValidatableObjectDemo()),
            ["validation-fluent-async"] = () => CodeSample(["FluentValidationAsyncDemo.cs"], Result: FluentValidationAsyncDemo()),
            ["validation-async"] = () => CodeSample(["AsyncValidationDemo.cs"], Result: AsyncValidationDemo()),
            ["validation-programmatic"] = () => CodeSample(["ProgrammaticValidateDemo.cs"], Result: ProgrammaticValidateDemo()),
            ["validation-first-error-wins"] = () => CodeSample(["FirstErrorWinsDemo.cs"], Result: FirstErrorWinsDemo()),
            ["validation-cross-field"] = () => CodeSample(["CrossFieldSummaryDemo.cs"], Result: CrossFieldSummaryDemo()),
            ["validation-nested-async"] = () => CodeSample(["NestedAsyncWithLiveTotalsDemo.cs"], Result: NestedAsyncWithLiveTotalsDemo()),

            // --- Forms guide: nested / complex models (their standalone /nested-forms page folded in). ---
            ["nested-subobject"] = () => CodeSample(["NestedSubObjectDemo.cs"], Result: NestedSubObjectDemo()),
            ["nested-list-foreach"] = () => CodeSample(["NestedListForeachDemo.cs"], Result: NestedListForeachDemo()),
            ["nested-list-indexer"] = () => CodeSample(["NestedListIndexerDemo.cs"], Result: NestedListIndexerDemo()),
            ["nested-fluent"] = () => CodeSample(["NestedFluentValidationDemo.cs"], Result: NestedFluentValidationDemo()),

            // --- Forms guide: radio/checkbox groups + multi-select example components. ---
            ["form-groups"] = () => CodeSample(["FormGroupsDemo.cs"], Result: FormGroupsDemo()),
            ["multi-select"] = () => CodeSample(["MultiSelectDemo.cs"], Result: MultiSelectDemo()),
            ["multi-select-controlled"] = () => CodeSample(["MultiSelectControlledDemo.cs"], Result: MultiSelectControlledDemo()),
            ["multi-select-checkbox"] = () => CodeSample(["MultiSelectCheckboxDemo.cs"], Result: MultiSelectCheckboxDemo()),
            ["multi-select-radio"] = () => CodeSample(["MultiSelectRadioDemo.cs"], Result: MultiSelectRadioDemo()),

            // --- Composition guide: context, callbacks, virtualize, keyed lists, drag & drop, error
            //     boundaries (their standalone example pages folded into docs/composition.md). ---
            ["context-theme"] = () => CodeSample(["ContextThemeDemo.cs"], Result: ContextThemeDemo()),
            ["callback-rating"] = () => CodeSample(["CallbackRatingDemo.cs"], Result: CallbackRatingDemo()),
            ["virtualize-items"] = () => CodeSample(["VirtualizeItemsDemo.cs"], Result: VirtualizeItemsDemo()),
            ["virtualize-provider"] = () => CodeSample(["VirtualizeProviderDemo.cs"], Result: VirtualizeProviderDemo()),
            ["keyed-lists-reorder"] = () => CodeSample(["KeyedListsReorderDemo.cs"], Result: KeyedListsReorderDemo()),
            ["drag-drop-sortable"] = () => CodeSample(["DragDropSortableDemo.cs"], Result: DragDropSortableDemo()),
            ["drag-drop-kanban"] = () => CodeSample(["DragDropKanbanDemo.cs"], Result: DragDropKanbanDemo()),
            ["boom-handler"] = () => CodeSample(["BoomHandlerDemo.cs"], Result: BoomHandlerDemo()),
            ["boom-render"] = () => CodeSample(["BoomRenderDemo.cs"], Result: BoomRenderDemo()),
            ["boom-nested"] = () => CodeSample(["BoomNestedDemo.cs"], Result: BoomNestedDemo()),

            // --- Lifecycle guide: hooks, mount/unmount cycle, disposal, cancellation, background
            //     service (their standalone example pages folded into docs/lifecycle.md). The demos
            //     embed the probe source — the teaching artifact — while Result mounts the live widget. ---
            ["lifecycle-hooks"] = () => CodeSample(["LifecycleProbe.cs"], Result: LifecycleProbe()),
            ["lifecycle-cycle"] = () => CodeSample(["LifecycleCycleProbe.cs"], Result: LifecycleCycleDemo()),
            ["disposal-sync"] = () => CodeSample(["DisposableTimerProbe.cs"], Result: DisposalSyncDemo()),
            ["disposal-async"] = () => CodeSample(["DisposableAsyncProbe.cs"], Result: DisposalAsyncDemo()),
            ["disposal-unmount"] = () => CodeSample(["UnmountTimerProbe.cs"], Result: DisposalUnmountDemo()),
            ["cancellation"] = () => CodeSample(["CancellationProbe.cs"], Result: CancellationDemo()),
            ["background-metrics"] = () => CodeSample(
                ["MetricsFeed.cs", "MetricsGauge.cs", "MetricsChart.cs"], Result: BackgroundMetricsDemo()),

            // --- JS-interop guide: element refs, scoped CSS, scoped JS / IJSRuntime, and the asset-
            //     loading story (their standalone example pages folded into docs/js-interop.md). ---
            ["js-interop-elementref"] = () => CodeSample(
                ["ElementRefDemo.cs", "ElementRefDemo.js"], Result: ElementRefDemo()),
            ["js-interop-scoped-css"] = () => CodeSample(
                ["ScopedRed.cs", "ScopedBlue.cs", "ScopedRed.css", "ScopedBlue.css"],
                Result: Div(Class: "d-flex flex-column gap-2")[ScopedRed(), ScopedBlue()]),
            ["js-interop-jsruntime"] = () => CodeSample(["JsRuntimeDemo.cs"], Result: JsRuntimeDemo()),

            // --- CQRS guide: one vertical slice (query + result-command + notification + a pipeline
            //     behaviour), dispatched reflection-free by the Rask.Cqrs source generator. ---
            ["cqrs-counter"] = () => CodeSample(
                ["CqrsCounterDemo.cs", "CounterSlice.cs"],
                Notes:
                "One slice, all four message shapes: a query (GetCounterState), a command that returns "
                + "a value (IncrementCounter), a notification the command publishes (CounterIncremented), "
                + "and a pipeline behaviour (DispatchLogBehavior) that wraps every dispatch — the "
                + "generator wires them with no runtime reflection.",
                Result: CqrsCounterDemo()),
            ["asset-basic-css"] = () => CodeSample(
                ["BasicScopedCss.cs", "BasicScopedCss.css"], Result: BasicScopedCss()),
            ["asset-js-only"] = () => CodeSample(["JsOnlyDemo.cs", "JsOnlyDemo.js"], Result: JsOnlyDemo()),
            ["asset-twin-bundle"] = () => CodeSample(
                ["TwinA.cs", "TwinA.css"], Result: Div(Class: "d-flex gap-2 flex-wrap")[TwinA(), TwinB()]),
            ["asset-lazy-mount"] = () => CodeSample(
                ["LazyMount.cs", "LazyChild.cs", "LazyChild.css"], Result: LazyMount()),

            // --- Bootstrap guide: the Rask.Bootstrap component showcase (its standalone example pages
            //     folded into docs/bootstrap.md). Each demo already lived in its own Bs*Demo.cs. ---
            ["bootstrap-nav"] = () => CodeSample(["BsNavDemo.cs"], Result: BsNavDemo()),
            ["bootstrap-buttons"] = () => CodeSample(["BsButtonsDemo.cs"], Result: BsButtonsDemo()),
            ["bootstrap-cards"] = () => CodeSample(["BsCardsDemo.cs"], Result: BsCardsDemo()),
            ["bootstrap-alerts"] = () => CodeSample(["BsAlertsDemo.cs"], Result: BsAlertsDemo()),
            ["bootstrap-icons"] = () => CodeSample(["BsIconsDemo.cs"], Result: BsIconsDemo()),
            ["bootstrap-modal"] = () => CodeSample(["BsModalDemo.cs"], Result: BsModalDemo()),
            ["bootstrap-tabs"] = () => CodeSample(["BsTabsDemo.cs"], Result: BsTabsDemo()),
            ["bootstrap-forms"] = () => CodeSample(["BsFormsDemo.cs"], Result: BsFormsDemo()),
            ["bootstrap-utilities"] = () => CodeSample(["BsUtilitiesDemo.cs"], Result: BsUtilitiesDemo()),
        };

    // Whether a demo key is registered (guides referencing an unknown key render a visible warning
    // and fail the registry-integrity unit test).
    public static bool Contains(string key) => Map.ContainsKey(key);

    // Builds a fresh instance of the demo. Must be called during render (LiveRenderContext ambient).
    public static Component Build(string key) => Map[key]();

    // All registered keys — used by the integrity test to assert no demo is orphaned.
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Map.Keys;
}
