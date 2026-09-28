using System;
using System.Collections.Generic;

namespace Rask.Generators.Translations;

// One key in a catalog: the dotted path a caller writes (Home.Title), the text, and where it came
// from so a diagnostic can point at the line rather than at the file.
internal sealed class CatalogEntry(string path, string value, int line, int column)
{
    public string Path { get; } = path;

    // The text, for an ordinary key. Empty for a plural entry, whose text lives per category.
    public string Value { get; } = value;

    public int Line { get; } = line;
    public int Column { get; } = column;

    /// <summary>
    ///     The counting parameter's name, when this key is a plural set rather than one string.
    /// </summary>
    /// <remarks>
    ///     A plural key is written as an object carrying <c>$plural</c>:
    ///     <c>{ "$plural": "count", "one": "{count} item", "other": "{count} items" }</c>.
    ///     The named parameter is what the category function counts, so it has to be a number rather
    ///     than the <c>object?</c> an ordinary placeholder defaults to.
    /// </remarks>
    public string? PluralParameter { get; init; }

    /// <summary>CLDR category to text, for a plural entry.</summary>
    public Dictionary<string, string> Forms { get; } = new(StringComparer.Ordinal);

    public bool IsPlural => PluralParameter is not null;
}
