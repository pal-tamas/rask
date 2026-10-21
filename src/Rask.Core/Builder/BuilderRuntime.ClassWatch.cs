using System.Runtime.InteropServices;

namespace Rask.Core;

public static partial class BuilderRuntime
{
    // A joined class's change report, held until the pass settles. `.Class("a").Class("b")` writes "a" first, which
    // differs from last pass's "a b" every time, so the ordinary per-write Track would dirty the component on every
    // render. Instead the value the pass STARTED from and the value it has reached so far are kept here, and
    // compared once, when the parent's Render() returns (DrainSlots).
    //
    // A watch exists only while those two differ at some write, so a lone `.Class(x)` that repeats last pass's
    // value never adds one — that path is a string compare. When none exists, the prop still holds the value the
    // pass started from, which is what lets a later join open a watch with the right Before. Per-thread and reused,
    // like the slot stack: the steady state allocates nothing.
    private record struct ClassWatchEntry(Component Target, ulong Bit, string? Before, string? After);

    [ThreadStatic]
    private static List<ClassWatchEntry>? _classWatches;

    private static void WatchClass(Component target, ulong bit, string? before, string? after)
    {
        var watches = CollectionsMarshal.AsSpan(_classWatches);
        for (var i = watches.Length - 1; i >= 0; i--)
        {
            ref var watch = ref watches[i];
            if (ReferenceEquals(watch.Target, target) && watch.Bit == bit)
            {
                watch.After = after;
                return;
            }
        }

        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            (_classWatches ??= new List<ClassWatchEntry>()).Add(new ClassWatchEntry(target, bit, before, after));
        }
    }

    // The provisional instance a Key step discarded: its watch moves to the kept one (CopyClass), and left here it
    // would never settle, holding the component for the life of the thread.
    private static void ForgetClass(Component target, ulong bit)
    {
        var watches = _classWatches;
        if (watches is null)
        {
            return;
        }

        for (var i = watches.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(watches[i].Target, target) && watches[i].Bit == bit)
            {
                watches.RemoveAt(i);
                return;
            }
        }
    }

    // Reports the change for every class prop of `target` whose final value differs from where the pass started.
    private static void SettleClasses(Component target)
    {
        var watches = _classWatches!;
        var write = 0;
        for (var read = 0; read < watches.Count; read++)
        {
            var watch = watches[read];
            if (!ReferenceEquals(watch.Target, target))
            {
                watches[write++] = watch;
                continue;
            }

            if (!string.Equals(watch.Before, watch.After, StringComparison.Ordinal))
            {
                target.MarkEntryPropsChangedInternal();
            }
        }

        watches.RemoveRange(write, watches.Count - write);
    }

    private static bool HasClassWatches => _classWatches is { Count: > 0 };
}
