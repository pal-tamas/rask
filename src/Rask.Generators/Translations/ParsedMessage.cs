using System.Collections.Generic;

namespace Rask.Generators.Translations;

// A message with its placeholders lifted out: "Hello, {name}!" becomes "Hello, {0}!" plus one
// placeholder called name.
internal sealed class ParsedMessage(string format, List<Placeholder> placeholders, string? error)
{
    public string Format { get; } = format;
    public List<Placeholder> Placeholders { get; } = placeholders;
    public string? Error { get; } = error;
}
