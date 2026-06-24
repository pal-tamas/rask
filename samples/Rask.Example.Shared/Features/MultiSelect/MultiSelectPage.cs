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
            "Selecting many values into a form model. MultiSelect<T> is a reusable example component — a "
            + "custom dropdown with removable chips — built on the public binding API (ExpressionAccessor + "
            + "BindingHelpers), so it binds to an ICollection and drives validation. CheckboxGroup<T> and "
            + "RadioGroup<T> are the built-in framework primitives for the same job."),
        H2(Class: "h4 mt-4 mb-3")["MultiSelect<T> — dropdown + chips"],
        CodeSample(
            ["MultiSelectDemo.cs", "MultiSelect.cs"],
            Notes: "Bound to a List<string> with a form-level \"pick at least two\" rule — open the dropdown, "
                + "pick options (they appear as chips), and submit to see validation.",
            Result: MultiSelectDemo()),
        H2(Class: "h4 mt-5 mb-3")["CheckboxGroup<T> — built-in, many values"],
        CodeSample(
            ["MultiSelectCheckboxDemo.cs"],
            Result: MultiSelectCheckboxDemo()),
        H2(Class: "h4 mt-5 mb-3")["RadioGroup<T> — built-in, single value"],
        CodeSample(
            ["MultiSelectRadioDemo.cs"],
            Result: MultiSelectRadioDemo())
    ];
}
