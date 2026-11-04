using Rask.Core.Forms;

namespace Rask.Core.Live;

// Which form control the walk is writing the attributes of while it is drawn disabled (#1273): the serializer arms
// it around one element's attributes (HtmlSerializer.SerializeElement), and every handler registered meanwhile is
// refused (LiveRenderContext.RegisterHandler → DisabledHandler). A browser sends no event to a disabled button,
// input, select or textarea, nor to one inside a <fieldset disabled> — except inside that fieldset's first <legend>,
// which HTML leaves enabled — so the walk also counts the disabled fieldsets it is inside.
//
// Held per thread, not on the context: a context is made every render, and a field on it is 8 more bytes every live
// update allocates (LiveUpdate20Rows, Baselines/allocation-budget.csv). The walk is synchronous on one thread, as
// LiveRenderContext.CurrentSync is, and each state is put back as its element is left, so the thread holds nothing
// between renders. The fieldsets are counted for the context that entered them: a render nested inside another
// starts outside any.
internal static class DisabledWalk
{
    [ThreadStatic] private static IFormControlElement? _refusing;
    [ThreadStatic] private static LiveRenderContext? _fieldsetsOf;
    [ThreadStatic] private static int _disabledFieldsets;
    [ThreadStatic] private static Component? _freeLegend;

    /// <summary>The disabled control whose attributes are being written, or null: every handler it registers is refused.</summary>
    internal static IFormControlElement? Refusing => _refusing;

    /// <summary>Whether <paramref name="walk" /> is inside a <c>&lt;fieldset disabled&gt;</c>.</summary>
    internal static bool InDisabledFieldset(LiveRenderContext walk) => ReferenceEquals(_fieldsetsOf, walk) && _disabledFieldsets > 0;

    /// <summary>
    ///     Arms the refusal for <paramref name="element" /> when it is a form control drawn disabled, itself or by its
    ///     fieldset; <paramref name="before" /> is what was armed, which <see cref="Disarm" /> puts back once its
    ///     attributes are written.
    /// </summary>
    internal static bool Refuses(LiveRenderContext walk, Component element, out IFormControlElement? before)
    {
        before = _refusing;
        if (element is not IFormControlElement control || (!InDisabledFieldset(walk) && !control.DrawnDisabled))
        {
            return false;
        }

        _refusing = control;
        return true;
    }

    /// <summary>Ends the refusal <see cref="Refuses" /> armed: the element's children register as their own.</summary>
    internal static void Disarm(IFormControlElement? before) => _refusing = before;

    /// <summary>
    ///     Enters <paramref name="element" />'s children: a disabled fieldset's disable what they hold, its first
    ///     legend's do not. Hands back what <see cref="Leave" /> restores.
    /// </summary>
    internal static Scope Enter(LiveRenderContext walk, Component element)
    {
        if (element is HTMLFieldSetElement { Disabled: true } fieldset)
        {
            var scope = Now();
            _disabledFieldsets = (ReferenceEquals(_fieldsetsOf, walk) ? _disabledFieldsets : 0) + 1;
            _fieldsetsOf = walk;
            _freeLegend = FirstLegend(fieldset);
            return scope;
        }

        if (element is HTMLLegendElement && ReferenceEquals(_fieldsetsOf, walk) && ReferenceEquals(element, _freeLegend))
        {
            var scope = Now();
            _disabledFieldsets--;
            _freeLegend = null;
            return scope;
        }

        return default;
    }

    /// <summary>Leaves the children <see cref="Enter" /> entered.</summary>
    internal static void Leave(Scope scope)
    {
        if (scope.Entered)
        {
            _fieldsetsOf = scope.Of;
            _disabledFieldsets = scope.Depth;
            _freeLegend = scope.Legend;
        }
    }

    private static Scope Now() => new(true, _fieldsetsOf, _disabledFieldsets, _freeLegend);

    private static HTMLLegendElement? FirstLegend(HTMLFieldSetElement fieldset)
    {
        foreach (var child in fieldset.Children ?? [])
        {
            if (child is HTMLLegendElement legend)
            {
                return legend;
            }
        }

        return null;
    }

    /// <summary>What a fieldset or its legend changed of the walk, to put back as its children are left.</summary>
    internal readonly record struct Scope(bool Entered, LiveRenderContext? Of, int Depth, Component? Legend);
}
