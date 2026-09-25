namespace Rask.Generators.Json;

/// <summary>The outcome of <see cref="JsonLite.Parse" />: a root, or the first defect and where it is.</summary>
internal readonly struct JsonParseResult(JsonNode? root, string? defect, int line, int column)
{
    public JsonNode? Root { get; } = root;

    public string? Defect { get; } = defect;

    public int Line { get; } = line;

    public int Column { get; } = column;
}
