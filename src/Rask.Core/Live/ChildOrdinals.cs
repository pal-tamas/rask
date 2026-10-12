namespace Rask.Core.Live;

/// <summary>
///     Which one of its type a child is: how many children of each type one component has written so far in
///     the render in flight.
/// </summary>
/// <remarks>
///     <para>
///         A child without a <c>Key</c> is identified by its type and its ORDER AMONG THE CHILDREN OF THAT
///         TYPE its owner writes — the owner being the component whose <c>Render()</c> builds it, since an
///         element pushes no scope of its own and a child is built before the element it will sit under
///         has its children. Counting among ALL the owner's children instead, as this used to, let one
///         child that came or went renumber every child after it: each later component was built again,
///         its fields reset and its <c>OnMount</c> re-run, for a paragraph appearing above it.
///     </para>
///     <para>
///         One array per component that builds children, grown to the number of types it writes and kept
///         for its life: a render allocates nothing here. A scan rather than a dictionary because that
///         number is small — a handful for a row, a few dozen for a page — and a scan of that many
///         references is cheaper than hashing one, at a fraction of the memory per component.
///     </para>
/// </remarks>
internal static class ChildOrdinals
{
    private const int InitialTypes = 4;

    /// <summary>The place the child now being built takes among those of <paramref name="type" />: 0, then 1…</summary>
    public static int Next(ref (Type? Type, int Written)[]? counts, Type type)
    {
        counts ??= new (Type?, int)[InitialTypes];
        for (var i = 0; i < counts.Length; i++)
        {
            ref var count = ref counts[i];
            if (count.Type == type)
            {
                return count.Written++;
            }

            if (count.Type is null)
            {
                count = (type, 1);
                return 0;
            }
        }

        var used = counts.Length;
        Array.Resize(ref counts, used * 2);
        counts[used] = (type, 1);
        return 0;
    }

    /// <summary>Starts the next render: every type counts from nought again, and one no longer written is forgotten.</summary>
    public static void Restart((Type? Type, int Written)[]? counts)
    {
        if (counts is not null)
        {
            Array.Clear(counts);
        }
    }
}
