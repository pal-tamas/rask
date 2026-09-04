using System.Text;

namespace Rask.Ui;

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
/// out in <see cref="UiClassNames" />: daisyUI emits a component's CSS only where Tailwind can see the
/// literal, so a name assembled at runtime would be absent from the sheet and its component would render
/// unstyled with nothing reporting it.
/// </para>
/// </remarks>
internal static class UiClass
{
    internal static string Compose(params string?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(part.Trim());
        }

        return sb.ToString();
    }
}
