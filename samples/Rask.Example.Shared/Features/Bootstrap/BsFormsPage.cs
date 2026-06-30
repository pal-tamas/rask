using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsFormsDemo" /> (BsInput/BsSelect/BsCheck + validation).</summary>
[Route("bootstrap/forms")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsFormsPage : Component
{
    protected override RenderResult Head => Title()["Forms — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Forms",
            "Bootstrap-styled form controls bound to a model. BsInput/BsSelect/BsCheck implement "
            + "IFormControl<T> — binding, .is-invalid and .invalid-feedback are built in."),
        CodeSample(["BsFormsDemo.cs"], Result: BsFormsDemo())
    ];
}
