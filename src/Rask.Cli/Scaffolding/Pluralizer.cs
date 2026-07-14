namespace Rask.Cli.Scaffolding;

/// <summary>
/// A deliberately small English pluralizer for turning a singular entity name into the plural used for
/// the DbSet, folder, route, and list page (Product → Products, Category → Categories, Box → Boxes).
/// Irregular plurals (person → people) aren't handled — a scaffolder favours predictability, and the
/// generated names are trivially renamed.
/// </summary>
internal static class Pluralizer
{
    public static string Pluralize(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        if (EndsWithAny(word, "s", "x", "z", "ch", "sh"))
        {
            return word + "es";
        }

        if (word.Length >= 2 && char.ToLowerInvariant(word[^1]) == 'y' && !IsVowel(word[^2]))
        {
            return word[..^1] + "ies";
        }

        return word + "s";
    }

    private static bool EndsWithAny(string word, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVowel(char c) => "aeiouAEIOU".Contains(c, StringComparison.Ordinal);
}
