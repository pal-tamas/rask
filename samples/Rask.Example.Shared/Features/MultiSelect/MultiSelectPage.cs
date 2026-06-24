using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("multiselect")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class MultiSelectPage : Component
{
    protected override RenderResult Head => Title()["Multi-select — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Multi-select",
            "Selecting many values. MultiSelect<T> is a reusable example component — a custom dropdown with "
            + "removable chips — built on the public binding API (ExpressionAccessor + BindingHelpers). It "
            + "supports two shapes: bound (an ICollection model property, with validation) and controlled "
            + "(Value + OnChange). CheckboxGroup<T> and RadioGroup<T> are smaller example components for the "
            + "same job. See the \"building form components\" guide for how to write your own."),
        H2(Class: "h4 mt-4 mb-3")["MultiSelect<T> — bound, dropdown + chips"],
        CodeSample(
            ["MultiSelectDemo.cs", "MultiSelect.cs"],
            Notes: "Bound to a List<string> with a per-field \"pick at least two\" Validate rule — open the "
                + "dropdown, pick options (they appear as chips), Esc or click outside to close, and submit "
                + "to see validation. The summary updates with no StateHasChanged.",
            Result: MultiSelectDemo()),
        H2(Class: "h4 mt-5 mb-3")["MultiSelect<T> — controlled (Value + OnChange)"],
        CodeSample(
            ["MultiSelectControlledDemo.cs"],
            Notes: "No Bind: the parent owns the selection and MultiSelect reports changes through OnChange.",
            Result: MultiSelectControlledDemo()),
        H2(Class: "h4 mt-5 mb-3")["CheckboxGroup<T> — example, many values"],
        CodeSample(
            ["MultiSelectCheckboxDemo.cs", "CheckboxGroup.cs"],
            Result: MultiSelectCheckboxDemo()),
        H2(Class: "h4 mt-5 mb-3")["RadioGroup<T> — example, single value"],
        CodeSample(
            ["MultiSelectRadioDemo.cs", "RadioGroup.cs"],
            Result: MultiSelectRadioDemo())
    ];
}
