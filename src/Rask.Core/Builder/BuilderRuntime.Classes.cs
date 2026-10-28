using System.Runtime.InteropServices;

namespace Rask.Core;

public static partial class BuilderRuntime
{
    /// <summary>
    ///     The class list <c>.Class("p-4", wide ? "w-full" : null)</c> writes: the parts joined by one space, each
    ///     trimmed, null and blank ones left out — or <c>null</c> when nothing is left, so no empty attribute renders.
    /// </summary>
    /// <remarks>
    ///     Exists because the one-string form made callers concatenate, and a missing space there
    ///     (<c>(wide ? "w-full" : "w-1/2") + "p-4"</c>) renders <c>w-fullp-4</c> — a class that styles nothing, with
    ///     nothing to say so. Sized once and written in place: this runs on every render of the element.
    /// </remarks>
    /// <param name="parts">The class names, in order.</param>
    /// <returns>The joined list, or <c>null</c> when every part was null or blank.</returns>
    public static string? JoinClasses(ReadOnlySpan<string?> parts)
    {
        var length = 0;
        var count = 0;
        string? only = null;
        foreach (var part in parts)
        {
            var trimmed = part.AsSpan().Trim();
            if (!trimmed.IsEmpty)
            {
                length += trimmed.Length;
                count++;
                only = part;
            }
        }

        if (count == 0)
        {
            return null;
        }

        // The usual call has one part left (a kit look with no app class, a class whose condition is off): hand
        // that string back rather than copy it on every render.
        if (count == 1 && only!.Length == length)
        {
            return only;
        }

        var buffer = length + count - 1 <= 256 ? stackalloc char[length + count - 1] : new char[length + count - 1];
        var at = 0;
        foreach (var part in parts)
        {
            var trimmed = part.AsSpan().Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            if (at > 0)
            {
                buffer[at++] = ' ';
            }

            trimmed.CopyTo(buffer[at..]);
            at += trimmed.Length;
        }

        return new string(buffer);
    }

    /// <summary>
    ///     The value a class-carrying step (<c>Class</c>, <c>ContainerClass</c>…) writes: <paramref name="value" />
    ///     itself on the chain's first write of this render, the two joined by <see cref="JoinClasses" />'s rules
    ///     on every later one — so <c>.Class("w-full").Class("mb-8")</c> is <c>w-full mb-8</c>.
    /// </summary>
    /// <remarks>
    ///     A chain re-runs every render against the instance the last render left, whose class is still on it —
    ///     a folding prop is put back only when the parent's <c>Render()</c> returns. So the first write is told
    ///     by the prop's PENDING bit, which the entry sets afresh on every pass, and it replaces rather than joins:
    ///     the list can never grow render after render. With no slot the receiver is either a fresh instance built
    ///     outside a render (no context: join onto its default) or one a past render built (replace, as before).
    ///     <para>
    ///         The prop change is judged on the FINAL list, when the pass settles (the class watch): the
    ///         first write of <c>.Class("a").Class("b")</c> always differs from last pass's <c>a b</c>.
    ///     </para>
    /// </remarks>
    /// <param name="target">The component the step writes.</param>
    /// <param name="bit">The prop's pending bit, or 0 for a prop the entry resets eagerly.</param>
    /// <param name="current">The prop's value now.</param>
    /// <param name="value">What the step was given.</param>
    /// <returns>The value to store.</returns>
    public static string? JoinClass(Component target, ulong bit, string? current, string? value)
    {
        var slots = CollectionsMarshal.AsSpan(_slots);
        for (var i = slots.Length - 1; i >= 0; i--)
        {
            ref var slot = ref slots[i];
            if (ReferenceEquals(slot.Target, target))
            {
                var written = value;
                if ((slot.PendingMask & bit) != 0)
                {
                    slot.PendingMask &= ~bit;
                }
                else
                {
                    written = JoinPair(current, value);
                }

                WatchClass(target, bit, current, written);
                return written;
            }
        }

        var result = Live.LiveRenderContext.Current is null ? JoinPair(current, value) : value;
        Track(target, current, result);
        return result;
    }

    /// <summary>
    ///     Replays a class-carrying prop the chain wrote before its <c>Key</c> step onto the instance the key
    ///     kept, moving its pending change report along with it.
    /// </summary>
    /// <param name="from">The provisional instance the chain wrote first.</param>
    /// <param name="to">The instance the key kept.</param>
    /// <param name="bit">The prop's pending bit.</param>
    /// <param name="before">The kept instance's value, as the last render left it.</param>
    /// <param name="value">The value the chain wrote so far.</param>
    /// <returns>The value to store on <paramref name="to" />.</returns>
    public static string? CopyClass(Component from, Component to, ulong bit, string? before, string? value)
    {
        ForgetClass(from, bit);
        WatchClass(to, bit, before, value);
        return value;
    }

    private static string? JoinPair(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return string.IsNullOrWhiteSpace(first) ? second : JoinClasses([first, second]);
    }
}
