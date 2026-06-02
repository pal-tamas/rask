namespace Rask.Core.Live;

/// <summary>
///     Ambient single-slot holder for a <see cref="Component.Key" /> being forwarded onto the
///     FIRST rendered element of a transparent component (a custom component / Fragment that
///     emits no tag of its own). Blazor's <c>@key</c> works on any component; the diff codec
///     only sees element frames, so a Component-level key has to land on the component's root
///     element. The serializer <see cref="Arm" />s the slot around a keyed transparent
///     component's body; the first element's <c>WriteAttributes</c> <see cref="Consume" />s it
///     and emits <c>data-rask-key</c>.
///     <para>
///         Single-threaded per render walk (same as <see cref="FrameSinkScope" />), so a
///         <see cref="ThreadStaticAttribute" /> is the right shape. The slot is cleared (not
///         restored) on dispose: a keyed component that renders no element simply drops its key
///         rather than leaking it onto a following sibling, and a nested keyed transparent
///         component's key wins over an outer one (innermost-to-the-element).
///     </para>
/// </summary>
public static class KeyForwardScope
{
    [ThreadStatic] private static string? _pending;

    /// <summary>
    ///     Arm the slot so the next element adopts <paramref name="key" /> as its
    ///     <c>data-rask-key</c>. The serializer calls this ONLY for a keyed transparent
    ///     component (never with a null key for a non-keyed one — that would wipe an ancestor's
    ///     armed key as it passes through), and pairs it with <see cref="Clear" /> in a finally.
    /// </summary>
    public static void Arm(string key) => _pending = key;

    /// <summary>Clear the slot. Paired with <see cref="Arm" /> after a keyed body serializes.</summary>
    public static void Clear() => _pending = null;

    /// <summary>
    ///     Return the pending forwarded key (or <c>null</c>) and clear the slot, so exactly one
    ///     element adopts it. Called by every element's attribute-writing path.
    /// </summary>
    public static string? Consume()
    {
        var k = _pending;
        _pending = null;
        return k;
    }
}
