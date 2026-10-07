namespace Rask;

/// <summary>What a <see cref="UiCheckboxGroup{T}" /> tells the checkboxes inside it.</summary>
/// <param name="GroupId">The group's id, which each checkbox's own id starts with.</param>
/// <param name="Variant">How the group draws its checkboxes.</param>
/// <param name="Disabled">Whether the whole group is disabled.</param>
/// <param name="Invalid">Whether the whole group is drawn invalid.</param>
/// <param name="Contains">Whether a checkbox's value is in the group's selection.</param>
/// <param name="Set">Puts a value into the selection, or takes it out.</param>
/// <param name="All">Whether every checkbox that can be ticked is.</param>
/// <param name="Some">Whether some are and some are not.</param>
/// <param name="SetAll">Ticks every checkbox that can be ticked, or clears them.</param>
internal sealed record UiCheckboxScope(
    string GroupId,
    Ui.CheckboxGroupVariant Variant,
    bool Disabled,
    bool Invalid,
    Func<object?, bool> Contains,
    Func<object?, bool, Task> Set,
    bool All,
    bool Some,
    Func<bool, Task> SetAll)
{
    /// <summary>
    ///     No group: what <see cref="UiCheckboxAll" /> hands the checkbox it draws, which belongs to the group
    ///     as its switch and not as one of its choices.
    /// </summary>
    internal static UiCheckboxScope None { get; } = new(
        "",
        Ui.CheckboxGroupVariant.Default,
        false,
        false,
        static _ => false,
        static (_, _) => Task.CompletedTask,
        false,
        false,
        static _ => Task.CompletedTask);

    /// <summary>The checkboxes written inside a group, in order, however deep in its markup.</summary>
    internal static IEnumerable<UiCheckbox> Members(IEnumerable<Component?>? children)
    {
        foreach (var child in children ?? [])
        {
            switch (child)
            {
                case UiCheckbox checkbox:
                    yield return checkbox;
                    break;
                case null or IUiFormControl:
                    break;
                default:
                    foreach (var nested in Members(child.Children))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }
}
