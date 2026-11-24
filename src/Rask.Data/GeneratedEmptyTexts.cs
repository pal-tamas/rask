using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     What a text kept empty (<c>KeepEmpty</c>) is written as when its field is left empty. Called by generated
///     code; not an API to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedEmptyTexts
{
    /// <summary>
    ///     The value object made from <c>''</c> past its rules, which would refuse an empty text — the way a stored
    ///     one is loaded.
    /// </summary>
    /// <typeparam name="TValue">The value object.</typeparam>
    /// <param name="make">Makes it from <c>''</c>.</param>
    /// <returns>What <paramref name="make" /> made.</returns>
    public static TValue Of<TValue>(Func<TValue> make)
    {
        ArgumentNullException.ThrowIfNull(make);

        using (RowLoading.Begin())
        {
            return make();
        }
    }
}
