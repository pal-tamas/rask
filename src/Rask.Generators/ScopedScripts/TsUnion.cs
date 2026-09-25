using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class TsUnion(List<ITsType> members) : ITsType
{
    public List<ITsType> Members { get; } = members;
}
