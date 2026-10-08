using Rask.Core.Components;

namespace Rask;

/// <summary>The checkbox that ticks or clears every checkbox of its group.</summary>
/// <remarks>
/// Flux UI's <c>flux:checkbox.all</c>, written among the checkboxes of a <see cref="UiCheckboxGroup{T}" />:
/// ticked when all of them are, clear when none is, and a dash while only some are. Pressing it ticks them
/// all, unless they all are already. Disabled checkboxes keep what they have.
/// </remarks>
public sealed partial class UiCheckboxAll : Component
{
    /// <summary>The words beside the box.</summary>
    public string? Label { get; set; }

    /// <summary>Help text under the label.</summary>
    public string? Description { get; set; }

    /// <summary>Cannot be pressed.</summary>
    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var group = Context.Get<UiCheckboxScope>() ?? UiCheckboxScope.None;

        // Its own box is no choice of the group's: it is drawn outside the group's scope.
        return Context.Provide(UiCheckboxScope.None)[
            Ui.Checkbox
                .Id(group.GroupId.Length == 0 ? null : UiFieldId.Choice(group.GroupId, "all"))
                .Label(Label)
                .Description(Description)
                .Checked(group.All)
                .Indeterminate(group.Some)
                .Disabled(Disabled == true || group.Disabled)
                .Class(Class)
                .OnChange(_ => group.SetAll(!group.All))
        ];
    }
}
