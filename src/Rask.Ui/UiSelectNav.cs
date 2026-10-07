using System.Globalization;

namespace Rask;

// The keyboard and id maths behind a drawn list of options — Ui.Select's listbox and combobox, a menu, a
// command palette: the option-id scheme that aria-activedescendant points at, and the roving cursor over
// the flat option list.
//
// Separated from the components because it is pure arithmetic with no markup in it, which makes it
// testable on its own — and the arithmetic is where this kind of control actually goes wrong. The
// cursor is a FLAT index over the options in the order they are drawn, so an arrow key moves to the next
// rendered option: the cursor follows the eye. Every mover takes a `disabled` predicate, so an option the
// cursor may not land on — disabled, or hidden by a search — is skipped over. A mover stops at the end of
// the list rather than wrapping, as Flux UI's does.
//
// Ported from the select-navigation helper of the Bootstrap package deleted in b349db4d, which is why
// it arrives already commented and already correct. Nothing of that package came with it: there is no
// markup here, no element type and no class name — the whole surface is integers and one id string.
internal static class UiSelectNav
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
}
