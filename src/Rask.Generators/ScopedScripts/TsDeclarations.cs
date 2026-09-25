using System;
using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

/// <summary>Everything a component's declaration file exports that the generator can act on.</summary>
internal sealed class TsDeclarations
{
    public List<TsFunctionDecl> Functions { get; } = new();
    public List<TsClassDecl> Classes { get; } = new();

    /// <summary>Interfaces and type aliases, exported or not — what a named type in a signature resolves to.</summary>
    public Dictionary<string, ITsType> Aliases { get; } = new(StringComparer.Ordinal);

    /// <summary><c>export const x = …</c> whose type is not a function — exported, but nothing to call.</summary>
    public List<string> NotCallable { get; } = new();
}
