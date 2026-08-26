using System;
using System.Collections.Generic;

namespace Rask.Generators.Translations;

// One key in a catalog: the dotted path a caller writes (Home.Title), the text, and where it came
// from so a diagnostic can point at the line rather than at the file.
internal sealed class CatalogEntry(string path, string value, int line, int column)
{
    public string Path { get; } = path;
    public string Value { get; } = value;
    public int Line { get; } = line;
    public int Column { get; } = column;
}

// One file: Resources/{Family}.{tag}.json.
internal sealed class Catalog(string filePath, string family, string cultureTag)
{
    public string FilePath { get; } = filePath;
    public string Family { get; } = family;

    // The BCP 47 tag from the file name. Every catalog carries one, including the neutral language's,
    // which is what makes "is this JSON a catalog?" a purely syntactic question — an app's own
    // Resources/seed-data.json is ignored without needing an orphan diagnostic to explain itself.
    public string CultureTag { get; } = cultureTag;

    public Dictionary<string, CatalogEntry> Entries { get; } = new(StringComparer.Ordinal);

    // Keys in the order they were read, so generated output is stable across builds.
    public List<string> Order { get; } = [];

    public List<CatalogDefect> Defects { get; } = [];

    public void Add(CatalogEntry entry)
    {
        if (Entries.ContainsKey(entry.Path))
        {
            Defects.Add(new CatalogDefect(
                $"duplicate key '{entry.Path}' — remove one of them", entry.Line, entry.Column));
            return;
        }

        Entries[entry.Path] = entry;
        Order.Add(entry.Path);
    }
}

// A hard problem with a catalog: it cannot generate code, or the code it generated would throw.
internal sealed class CatalogDefect(string reason, int line, int column)
{
    public string Reason { get; } = reason;
    public int Line { get; } = line;
    public int Column { get; } = column;
}
