using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class TsNamed(string name, List<ITsType>? args = null) : ITsType
{
    public string Name { get; } = name;
    public List<ITsType> Args { get; } = args ?? new List<ITsType>();
}
