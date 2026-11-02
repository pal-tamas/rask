using Rask.Core.Forms;

namespace Rask;

/// <summary>The marks of Flux's <c>mask:dynamic="$money($input)"</c>, checked once for every control that takes them.</summary>
internal static class UiMoney
{
    /// <summary>The marks a <c>Money(...)</c> step was handed, or an exception naming what is wrong with them.</summary>
    /// <param name="decimals">The decimal mark, one character.</param>
    /// <param name="thousands">The mark between groups of three digits, one character.</param>
    /// <param name="precision">How many decimals can be typed; 0 for whole amounts.</param>
    internal static MoneyMarks Marks(string decimals, string thousands, int precision)
    {
        ArgumentException.ThrowIfNullOrEmpty(decimals);
        ArgumentException.ThrowIfNullOrEmpty(thousands);
        ArgumentOutOfRangeException.ThrowIfNegative(precision);
        if (decimals.Length != 1 || thousands.Length != 1 || string.Equals(decimals, thousands, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Money takes one character for each mark, and two different ones: not \"{decimals}\" and \"{thousands}\".",
                nameof(thousands));
        }

        return new MoneyMarks(decimals[0], thousands[0], precision);
    }
}
