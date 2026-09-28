using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class MappedParameter
{
    public string? Type { get; init; }

    /// <summary>A callback's async-handler form; null for any other parameter.</summary>
    public string? AsyncType { get; init; }

    public string? ArgFormat { get; init; }
    public bool Optional { get; init; }
    public string DefaultValue { get; init; } = " = null";
    public string? Error { get; init; }
    public List<string> Records { get; init; } = new();
}
