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
        foreach (var part in parts)
        {
            var trimmed = part.AsSpan().Trim();
            if (!trimmed.IsEmpty)
            {
                length += trimmed.Length;
                count++;
            }
        }

        if (count == 0)
        {
            return null;
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
}
