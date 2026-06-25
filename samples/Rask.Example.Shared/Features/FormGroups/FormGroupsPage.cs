using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("form-groups")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class FormGroupsPage : Component
{
    protected override RenderResult Head => Title()["Radio & checkbox groups — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Radio & checkbox groups",
            "Pick one value from a set of radios, or many into a collection of checkboxes — example Components built on the public binding API, with the same bound and controlled shapes as MultiSelect<T>."),
        H2(Class: "h4 mt-4 mb-3")["RadioGroup + CheckboxGroup"],
        CodeSample(
            ["FormGroupsDemo.cs", "RadioGroup.cs", "CheckboxGroup.cs"],
            Notes:
            "Shown in controlled mode (Value + OnChange): the parent owns the selection and OnChange (auto-wrapped) re-renders the demo, so the readout stays live. Each item is Bootstrap form-check markup; ItemClass adds wrapper classes like form-check-inline.",
            Result: FormGroupsDemo()),
        H2(Class: "h4 mt-5 mb-3")["Notes"],
        Ul(Class: "text-secondary")[
            Li()[
                "Two modes, like MultiSelect: bound — RadioGroup(() => model.Plan, …) / CheckboxGroup<T>(() => model.Tags, …) — two-way binds the model and runs a per-field Validate rule; controlled — Value + OnChange — the parent owns the value (used above)."],
            Li()[
                "They're Components, so their own checks/radios update on toggle; host-side derived UI (the summary) updates via the auto-wrapped controlled OnChange."],
            Li()[
                "In bound mode, validation rides the field: each change calls NotifyFieldChanged + ValidateFieldAsync, so a Validate rule (or DataAnnotations/FluentValidation) on the bound property applies and surfaces via the embedded ValidationMessage."]
        ]
    ];
}
