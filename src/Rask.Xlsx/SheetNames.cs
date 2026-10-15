using System.Buffers;

namespace Rask.Workbooks;

/// <summary>Excel's rules for a sheet's name, checked up front: a workbook with a bad one opens as "repaired" or not at all.</summary>
internal static class SheetNames
{
    private const int MaxLength = 31;
    private static readonly SearchValues<char> Forbidden = SearchValues.Create("[]:*?/\\");

    public static void Check(Xlsx.Sheet[] sheets)
    {
        if (sheets.Length == 0)
        {
            throw new ArgumentException("A workbook needs at least one sheet.", nameof(sheets));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            if (sheet is null)
            {
                throw new ArgumentNullException(nameof(sheets), "A sheet is null.");
            }

            Check(sheet.Name);
            if (!seen.Add(sheet.Name))
            {
                throw new ArgumentException($"Two sheets are named '{sheet.Name}'; Excel compares names without regard to case.", nameof(sheets));
            }

            if (sheet.Headings.Count == 0)
            {
                throw new ArgumentException($"Sheet '{sheet.Name}' has no headings.", nameof(sheets));
            }
        }
    }

    private static void Check(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A sheet needs a name.", nameof(name));
        }

        if (name.Length > MaxLength)
        {
            throw new ArgumentException($"The sheet name '{name}' is {name.Length} characters; Excel allows {MaxLength}.", nameof(name));
        }

        if (name.AsSpan().IndexOfAny(Forbidden) >= 0)
        {
            throw new ArgumentException($"The sheet name '{name}' holds one of [ ] : * ? / \\, which Excel does not allow.", nameof(name));
        }

        if (name[0] == '\'' || name[^1] == '\'')
        {
            throw new ArgumentException($"The sheet name '{name}' begins or ends with an apostrophe, which Excel does not allow.", nameof(name));
        }

        if (name.Any(char.IsControl))
        {
            throw new ArgumentException("A sheet name cannot hold a control character.", nameof(name));
        }
    }
}
