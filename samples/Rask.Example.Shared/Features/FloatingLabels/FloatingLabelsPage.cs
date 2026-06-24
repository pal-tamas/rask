using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("floating-labels")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class FloatingLabelsPage : Component
{
    protected override RenderResult Head => Title()["Floating labels — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Floating labels",
            "FloatingInput, FloatingSelect, and FloatingTextarea are reusable example components (not framework code) that wrap Rask's Input/Select/Textarea + Label + ValidationMessage in Bootstrap 5.3's .form-floating markup. They own no validation state and need no extra CSS — DataAnnotationsValidator() drives the messages, shown via Bootstrap's own .invalid-feedback .d-block utilities. One line per field, with the input type inferred from the bound property."),
        H2(Class: "h4 mt-4 mb-3")["A floating-label form"],
        CodeSample(
            ["FloatingLabelsDemo.cs"],
            Notes:
            "Each field is a single FloatingInput(() => model.X, \"Label\") call. The id linking <label> to <input> is derived from the property name, and validation messages render only when the field is invalid — submit empty to see the feedback appear under each field.",
            Result: FloatingLabelsDemo())
    ];
}
