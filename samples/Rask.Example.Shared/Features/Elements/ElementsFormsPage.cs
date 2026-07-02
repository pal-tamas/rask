using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/forms")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsFormsPage : Component
{
    protected override Component? Head => Title()["Form elements — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Form elements",
            "Every form-associated element: form, fieldset/legend, label, input, select/optgroup/option, "
            + "textarea, datalist, output, progress, meter, button. See the Forms page for binding & validation."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsFormsDemo.cs"], Result: ElementsFormsDemo())
    ];
}
