using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class TsFunctionDecl(string name, List<TsParam> parameters, ITsType returns, bool generic, string? doc)
{
    public string Name { get; } = name;
    public List<TsParam> Parameters { get; } = parameters;
    public ITsType Returns { get; } = returns;
    public bool Generic { get; } = generic;
    public string? Doc { get; } = doc;
}
