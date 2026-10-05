namespace Rask.Core.Dom.Build;

// The value a generated keyword enum gives each member: the 32-bit FNV-1a hash of the keyword it spells. Stable across
// refreshes — a keyword the spec adds, removes or reorders moves no other member's value — and the same in every build.
internal static class Fnv1a
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static int Hash(string keyword)
    {
        var hash = OffsetBasis;
        foreach (var c in keyword)
        {
            // Keywords are ASCII, so a char is its UTF-8 byte.
            if (c > 0x7F)
            {
                throw new DomEmitException($"the keyword `{keyword}` is not ASCII, so its enum value has no stable hash");
            }

            hash = unchecked((hash ^ c) * Prime);
        }

        return unchecked((int)hash);
    }
}
