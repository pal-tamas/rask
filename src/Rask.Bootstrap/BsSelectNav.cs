using System.Globalization;

namespace Rask.Bootstrap;

// Shared keyboard/id helpers for the Bs select family (BsSelect + BsMultiSelect). The two controls cannot
// share a base class (BsSelectBase<TValue,TItem> : BsFormControl<TValue> vs BsMultiSelect<TItem> : BsBlock),
// so the option-id scheme (for aria-activedescendant) and the roving-cursor math over the flat option list
// live here as a static helper both consume. The cursor is a flat index; every mover takes a `disabled`
// predicate so a per-option-disabled option is skipped over rather than landed on.
internal static class BsSelectNav
{
    // The stable per-option id an aria-activedescendant points at: "{prefix}-opt-{flatIndex}".
    internal static string OptId(string prefix, int idx) =>
        prefix + "-opt-" + idx.ToString(CultureInfo.InvariantCulture);

    // First/last option index the keyboard cursor may land on, skipping disabled options; -1 if all disabled.
    internal static int FirstEnabled(int count, Func<int, bool> disabled)
    {
        for (var i = 0; i < count; i++)
        {
            if (!disabled(i))
            {
                return i;
            }
        }

        return -1;
    }

    internal static int LastEnabled(int count, Func<int, bool> disabled)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            if (!disabled(i))
            {
                return i;
            }
        }

        return -1;
    }

    // Moves the cursor one enabled option in `dir` (±1), skipping disabled ones; stays put when there is no
    // enabled option that way (so ArrowDown at the last enabled option is a no-op, not a wrap-around).
    internal static int Step(int cursor, int dir, int count, Func<int, bool> disabled)
    {
        for (var i = cursor + dir; i >= 0 && i < count; i += dir)
        {
            if (!disabled(i))
            {
                return i;
            }
        }

        return cursor;
    }

    // The cursor to seed when the popover opens: the selected option if it is in range and enabled, else the
    // first enabled option (-1 when every option is disabled/there are none).
    internal static int Seed(int selected, int count, Func<int, bool> disabled) =>
        selected >= 0 && selected < count && !disabled(selected)
            ? selected
            : FirstEnabled(count, disabled);

    // Clamps a possibly-stale cursor back into the current (filtered) list and off any disabled option, so
    // mutation points can set _cursor loosely (e.g. 0 on a filter change) and let render snap it once. Prefers
    // the nearest enabled option forward, then backward; -1 when nothing is selectable.
    internal static int Normalize(int cursor, int count, Func<int, bool> disabled)
    {
        if (count == 0)
        {
            return -1;
        }

        if (cursor < 0)
        {
            return FirstEnabled(count, disabled);
        }

        if (cursor >= count)
        {
            return LastEnabled(count, disabled);
        }

        if (!disabled(cursor))
        {
            return cursor;
        }

        var forward = Step(cursor, 1, count, disabled);
        if (forward != cursor)
        {
            return forward;
        }

        var backward = Step(cursor, -1, count, disabled);
        return backward != cursor ? backward : -1;
    }
}
