using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class MappedReturn
{
    public ReturnKind Kind { get; init; }
    public string? Type { get; init; }

    /// <summary>For <see cref="ReturnKind.Tuple" />: each element's C# type, in order.</summary>
    public List<string> Elements { get; init; } = new();
    public string? Error { get; init; }
    public List<string> Records { get; init; } = new();
}
