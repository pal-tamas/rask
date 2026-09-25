namespace Rask.Core.Globalization;

/// <summary>
///     Supplies translated text for the framework's own strings. Implemented by the generated catalog.
/// </summary>
public interface IRaskStringSource
{
    /// <summary>
    ///     The text for <paramref name="key" /> in <paramref name="cultureTag" />, or <c>null</c> to use
    ///     the framework's English default.
    /// </summary>
    string? Get(RaskString key, string cultureTag);
}
