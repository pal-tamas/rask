using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class TsFunction(List<TsParam> parameters, ITsType returns, bool generic) : ITsType
{
    public List<TsParam> Parameters { get; } = parameters;
    public ITsType Returns { get; } = returns;
    public bool Generic { get; } = generic;
}
