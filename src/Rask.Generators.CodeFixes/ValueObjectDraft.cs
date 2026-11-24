using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// The value object RASK130's fix declares for a bare property, and what it takes off the property for it: the record's
// declaration as written, the attributes it makes redundant, and each FluentValidation chain with the calls it keeps.
internal sealed class ValueObjectDraft(
    string name,
    string declaration,
    bool nullable,
    bool needsUsing,
    IReadOnlyList<AttributeSyntax> redundantAttributes,
    IReadOnlyList<FluentChainEdit> chains)
{
    // SupplierName: the containing type's name, then the property's.
    public string Name { get; } = name;

    // The record as written, from `public sealed partial record` to its semicolon.
    public string Declaration { get; } = declaration;

    // Whether the property may be left empty: it is then typed SupplierName?.
    public bool Nullable { get; } = nullable;

    // Whether the file needs `using Rask.Values;` for the base record to bind.
    public bool NeedsUsing { get; } = needsUsing;

    public IReadOnlyList<AttributeSyntax> RedundantAttributes { get; } = redundantAttributes;

    public IReadOnlyList<FluentChainEdit> Chains { get; } = chains;
}
