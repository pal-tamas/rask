namespace Rask.Generators.Json;

/// <summary>The kind of a <see cref="JsonNode" />.</summary>
internal enum JsonKind
{
    Object,
    Array,
    String,
    Number,
    True,
    False,
    Null,
}
