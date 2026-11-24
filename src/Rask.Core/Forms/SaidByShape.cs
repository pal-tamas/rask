namespace Rask.Core.Forms;

// An attribute a bound control is given by its field's shape, unless the author set it: `said` remembers that the
// value is the framework's, so it follows the shape as it changes and goes once the shape stops saying it.
internal static class SaidByShape
{
    internal static string? Text(string? shaped, string? current, ref bool said)
    {
        if (shaped is not null && (current is null || said))
        {
            said = true;
            return shaped;
        }

        if (shaped is null && said)
        {
            said = false;
            return null;
        }

        return current;
    }

    internal static TValue? Value<TValue>(TValue? shaped, TValue? current, ref bool said)
        where TValue : struct
    {
        if (shaped is not null && (current is null || said))
        {
            said = true;
            return shaped;
        }

        if (shaped is null && said)
        {
            said = false;
            return null;
        }

        return current;
    }
}
