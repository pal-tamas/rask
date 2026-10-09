namespace Rask.Site.Features;

// The rules for a destination's name live with the value, so the form and the domain ask the same question.
public readonly record struct DestinationName(string Value)
{
    public const int MaxLength = 255;

    public static IEnumerable<string> Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return "A destination needs a name.";
        }
        else if (value.Length > MaxLength)
        {
            yield return $"A name is at most {MaxLength} characters.";
        }
        else if (!char.IsLetter(value[0]))
        {
            yield return "A name starts with a letter.";
        }
    }
}
