namespace Rask.Data;

/// <summary>How a full-text index splits text into the terms a search matches.</summary>
public enum FullTextTokenizer
{
    /// <summary>
    /// Splits on Unicode word boundaries, case-insensitively, and ignores diacritics — so <c>keres</c> finds
    /// <c>kérés</c> and <c>cafe</c> finds <c>café</c>. Words must match whole, or as a prefix of the last word
    /// typed. The right choice for most apps and for any language.
    /// </summary>
    Unicode,

    /// <summary>
    /// <see cref="Unicode"/>, plus English stemming: <c>run</c> also finds <c>running</c> and <c>runs</c>.
    /// Stemming non-English text makes matches worse, not better, so use it only for English content.
    /// </summary>
    English,
}
