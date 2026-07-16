using System.Globalization;

namespace Rask.Bootstrap;

// Shared keyboard/id helpers for the Bs select family (BsSelect + BsMultiSelect). The two controls cannot
// share a base class (BsSelectBase<TValue,TItem> : BsFormControl<TValue> vs BsMultiSelect<TItem> : BsBlock),
// so the option-id scheme (for aria-activedescendant) and the roving-cursor math over the flat option list
// live here as a static helper both consume. The cursor is a flat index; every mover takes a `disabled`
// predicate so a per-option-disabled option is skipped over rather than landed on.
internal static class BsSelectNav
{
    // One rendered option and its position in the FLAT cursor space (== render order after grouping).
    internal readonly record struct FlatRow<TItem>(TItem Item, int FlatIndex);

    // A group of options under an optional header (null header == the ungrouped single group).
    internal readonly record struct OptGroup<TItem>(string? Header, IReadOnlyList<FlatRow<TItem>> Rows);

    // The rendered layout: groups (render order: header then Rows) plus the flat option list the roving cursor
    // indexes. Flat[i] is the i-th rendered option, so the flat index doubles as the aria-activedescendant id.
    internal readonly record struct Layout<TItem>(
        IReadOnlyList<OptGroup<TItem>> Groups,
        IReadOnlyList<TItem> Flat);

    // Groups the already-filtered options for rendering while preserving a flat option list for cursor math.
    // group == null → one headerless group and Flat == filtered. Otherwise options are bucketed by the group
    // key in first-seen order; each option is assigned the next flat index so the flat list tracks final render
    // order (headers are not in it). Empty groups can't arise — a key is created only on its first member.
    internal static Layout<TItem> Build<TItem>(IReadOnlyList<TItem> filtered, Func<TItem, string>? group)
    {
        if (group is null)
        {
            var rows = new FlatRow<TItem>[filtered.Count];
            for (var i = 0; i < filtered.Count; i++)
            {
                rows[i] = new FlatRow<TItem>(filtered[i], i);
            }

            return new Layout<TItem>([new OptGroup<TItem>(null, rows)], filtered);
        }

        // First pass: bucket items by group key in first-seen order (buckets hold the items, not yet indexed).
        var order = new List<string>();
        var buckets = new Dictionary<string, List<TItem>>();
        foreach (var item in filtered)
        {
            var key = group(item);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets[key] = bucket;
                order.Add(key);
            }

            bucket.Add(item);
        }

        // Second pass: lay the groups out in first-seen order and assign each option its flat index IN THAT
        // RENDER ORDER, so the cursor's flat index equals the option's rendered position (arrows follow the eye).
        var flat = new List<TItem>(filtered.Count);
        var groups = new OptGroup<TItem>[order.Count];
        for (var g = 0; g < order.Count; g++)
        {
            var items = buckets[order[g]];
            var rows = new FlatRow<TItem>[items.Count];
            for (var j = 0; j < items.Count; j++)
            {
                rows[j] = new FlatRow<TItem>(items[j], flat.Count);
                flat.Add(items[j]);
            }

            groups[g] = new OptGroup<TItem>(order[g], rows);
        }

        return new Layout<TItem>(groups, flat);
    }

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
