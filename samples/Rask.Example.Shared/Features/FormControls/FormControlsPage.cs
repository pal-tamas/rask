using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("form-controls")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class FormControlsPage : Component
{
    protected override RenderResult Head => Title()["Form controls — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Form controls",
            "Every form control in both shapes — controlled (Value + OnChange, the parent owns the state) and bound (two-way Bind through the EditContext). Change either side and its readout updates live, with no StateHasChanged."),
        H2(Class: "h4 mt-4 mb-3")["Select"],
        CodeSample(
            ["FormControlsSelectDemo.cs"],
            Result: FormControlsSelectDemo()),
        H2(Class: "h4 mt-5 mb-3")["Input — text"],
        CodeSample(
            ["FormControlsInputDemo.cs"],
            Notes: "Controlled Value + OnChange commits on blur/Enter; the bound Input streams per keystroke.",
            Result: FormControlsInputDemo()),
        H2(Class: "h4 mt-5 mb-3")["Textarea"],
        CodeSample(
            ["FormControlsTextareaDemo.cs"],
            Result: FormControlsTextareaDemo()),
        H2(Class: "h4 mt-5 mb-3")["Radio group"],
        CodeSample(
            ["FormControlsRadioDemo.cs", "BsRadioGroup.cs"],
            Result: FormControlsRadioDemo()),
        H2(Class: "h4 mt-5 mb-3")["Checkbox group"],
        CodeSample(
            ["FormControlsCheckboxDemo.cs", "BsCheckboxGroup.cs"],
            Result: FormControlsCheckboxDemo()),
        H2(Class: "h4 mt-5 mb-3")["Multi-select"],
        CodeSample(
            ["FormControlsMultiSelectDemo.cs", "BsMultiSelect.cs"],
            Result: FormControlsMultiSelectDemo())
    ];
}
