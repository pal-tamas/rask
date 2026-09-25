using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

/// <summary>An object shape — an interface body or a <c>type X = { … }</c>.</summary>
internal sealed class TsObject : ITsType
{
    public List<TsProperty> Properties { get; } = new();
}
