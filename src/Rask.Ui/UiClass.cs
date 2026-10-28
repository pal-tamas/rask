namespace Rask;

/// <summary>
/// Joins the parts of a class attribute.
/// </summary>
/// <remarks>
/// <para>
/// Every component builds its class list from a base name plus whichever of colour, fill, size and the
/// call site's own extras are present, and most of those are absent most of the time. Interpolating them
/// directly produces runs of spaces and a trailing one on nearly every element — harmless to a browser,
/// noisy in a diff, and enough to break an assertion that expects an exact attribute.
/// </para>
/// <para>
/// This does no other work. In particular it never BUILDS a class name, only joins ones already spelled
/// out as whole literals: Tailwind emits a class only where it can see the
/// literal, so a name assembled at runtime would be absent from the sheet and its component would render
/// unstyled with nothing reporting it.
/// </para>
/// </remarks>
internal static class UiClass
{
    internal static string Compose(params ReadOnlySpan<string?> parts)
    {
        // The commonest list is one part with nothing beside it: that string itself, no copy.
        string? only = null;
        var count = 0;
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                only = part;
                if (++count > 1)
                {
                    break;
                }
            }
        }

        if (count == 1 && only!.Length == only.AsSpan().Trim().Length)
        {
            return only;
        }

        return BuilderRuntime.JoinClasses(parts) ?? string.Empty;
    }
}
