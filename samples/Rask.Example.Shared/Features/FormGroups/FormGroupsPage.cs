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
            "Bind a set of radios to one value, or a set of checkboxes to a collection — one call each, wired into the same EditContext as Input(Bind: …)."),
        H2(Class: "h4 mt-4 mb-3")["RadioGroup + CheckboxGroup"],
        CodeSample(
            ["FormGroupsDemo.cs"],
            Notes:
            "Both parse the bind expression, resolve the ambient EditContext, and wire each input's change handler to set the value (radios) or add/remove the item (checkboxes), then re-validate. They render a transparent Fragment of <label><input>…</label>, so you control layout with OptionLabel and ItemClass.",
            Result: FormGroupsDemo()),
        H2(Class: "h4 mt-5 mb-3")["Notes"],
        Ul(Class: "text-secondary")[
            Li()[
                "RadioGroup binds a single TValue; the option equal to the current value renders checked. CheckboxGroup binds an ICollection<TItem>; the collection is mutated in place on toggle."],
            Li()[
                "Changing an option re-renders the component that declared the group (the change handler's owner), so a summary like the one above updates immediately."],
            Li()[
                "Validation rides the same field: each change calls NotifyFieldChanged + ValidateFieldAsync, so DataAnnotations/FluentValidation rules on the bound property apply."]
        ]
    ];
}
