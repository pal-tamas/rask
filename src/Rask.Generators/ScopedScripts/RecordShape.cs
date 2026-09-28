using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class RecordShape(string tsName, string csName, string? doc)
{
    public string TsName { get; } = tsName;
    public string CsName { get; } = csName;
    public string? Doc { get; } = doc;
    public List<(string CsName, string JsonName, string Type, bool Required)> Properties { get; } = new();
}
