using Rask.Values;

namespace Rask.Site.Features;

// The rules for a destination's name live with the value, so the form and the domain ask the same question: filled,
// at most MaxLength characters, and starting with a letter.
public sealed partial record DestinationName(string Value)
    : Text(Value, MaxLength: MaxLength, Pattern: @"^\p{L}", Message: "A name starts with a letter.")
{
    public const int MaxLength = 255;
}
