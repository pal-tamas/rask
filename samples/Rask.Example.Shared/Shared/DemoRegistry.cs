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
        };

    // Whether a demo key is registered (guides referencing an unknown key render a visible warning
    // and fail the registry-integrity unit test).
    public static bool Contains(string key) => Map.ContainsKey(key);

    // Builds a fresh instance of the demo. Must be called during render (LiveRenderContext ambient).
    public static Component Build(string key) => Map[key]();

    // All registered keys — used by the integrity test to assert no demo is orphaned.
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Map.Keys;
}
