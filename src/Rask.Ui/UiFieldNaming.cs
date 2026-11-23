using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     The words a hand-composed <see cref="UiField" /> draws for itself: holding a bound control and no
///     <see cref="UiLabel" />, it is labelled by the member — its <c>[Display(Name)]</c>, the badge the control's own label
///     would carry, its <c>[Display(Description)]</c> where the field holds no <see cref="UiDescription" /> — placed where
///     the control's own label goes.
/// </summary>
internal static class UiFieldNaming
{
    /// <summary>The field's parts with the member's label and help text added, or the parts as given.</summary>
    /// <param name="children">The parts the call site wrote.</param>
    internal static Parts Of(IEnumerable<Component?>? children)
    {
        // Read more than once, and matched by reference: a lazy sequence would build its parts again on each read.
        var given = children as IReadOnlyCollection<Component?> ?? children?.ToList();
        if (given is null || Named(given) is not { } control)
        {
            return new Parts(given, null);
        }

        var label = DisplayNames.Of(control.Bound) is { } name ? Ui.Label.Badge(UiWithField.BadgeOf(control))[name] : null;
        var description = given.Any(child => child is UiDescription) ? null : Described(control);
        if (label is null && description is null)
        {
            return new Parts(given, null);
        }

        List<Component?> parts = [];
        foreach (var child in given)
        {
            if (ReferenceEquals(child, control))
            {
                parts.AddRange(control.ControlFirst ? [child, label, description] : [label, description, child]);
            }
            else
            {
                parts.Add(child);
            }
        }

        return new Parts(parts, control.FieldVariant);
    }

    // The field's control, where it is bound, labels itself by nothing, and the field holds no label: the same control
    // UiFieldScope finds — the first kit control, unless a plain element with an id comes before it.
    private static IUiFieldControl? Named(IEnumerable<Component?> children)
    {
        IUiFieldControl? found = null;
        foreach (var child in children)
        {
            switch (child)
            {
                case UiLabel:
                    return null;
                case IUiFieldControl control when found is null:
                    found = control;
                    break;
                case Element { Id: not null } when found is null:
                    return null;
            }
        }

        return found is not null && Unworded(found) ? found : null;
    }

    /// <summary>
    ///     The label a field around <paramref name="control" /> would draw for it — its member's <c>[Display]</c> name — or
    ///     null for a control that has words of its own, or a member with no name.
    /// </summary>
    internal static string? LabelOf(IUiFieldControl control) => Unworded(control) ? DisplayNames.Of(control.Bound) : null;

    // Bound, and given no label or description of its own: a control with either draws its own field.
    private static bool Unworded(IUiFieldControl control) =>
        control is { Bound: not null } and not IUiFormControl { Label: not null } and not IUiFormControl { Description: not null };

    private static Component? Described(IUiFieldControl control) =>
        DisplayNames.DescriptionOf(control.Bound) is { } text ? Ui.Description[text] : null;

    /// <summary>A field's parts, and the layout its control asks for when the field labels it.</summary>
    /// <param name="Children">What the field draws.</param>
    /// <param name="Variant">The control's own field layout, where the field drew its label; null otherwise.</param>
    internal readonly record struct Parts(IEnumerable<Component?>? Children, Ui.FieldVariant? Variant);
}
