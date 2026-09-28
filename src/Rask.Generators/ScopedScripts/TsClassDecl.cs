using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class TsClassDecl(string name, string? doc, bool generic, bool isAbstract)
{
    public string Name { get; } = name;
    public string? Doc { get; } = doc;
    public bool Generic { get; } = generic;
    public bool IsAbstract { get; } = isAbstract;
    public List<List<TsParam>> Constructors { get; } = new();
    public List<TsFunctionDecl> Methods { get; } = new();
}
