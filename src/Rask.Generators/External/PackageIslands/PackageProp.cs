using System;
using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>One package prop, ready for both generators.</summary>
/// <param name="Name">The prop's name in the package.</param>
/// <param name="Wire">The JSON key it is written under.</param>
/// <param name="ClrName">The C# property and chain step.</param>
/// <param name="ChainTypeFqn">The property's type, nullable unless required.</param>
/// <param name="IsRequired">Whether the chain requires it.</param>
/// <param name="Nullable">Whether null is a legal value for the package.</param>
/// <param name="DeclaredByUser">Whether the author declared it by hand, so it is written but not declared.</param>
/// <param name="Type">The mapped value type, or null for a callback.</param>
/// <param name="Callback">The callback shape, or null for a value.</param>
/// <param name="Summary">The XML text of its summary, escaped and on one line.</param>
/// <param name="Doc">The package's documentation, for the multi-line doc comment.</param>
/// <param name="Default">The package's documented default.</param>
internal sealed record PackageProp(
    string Name,
    string Wire,
    string ClrName,
    string ChainTypeFqn,
    bool IsRequired,
    bool Nullable,
    bool DeclaredByUser,
    CsType? Type,
    CallbackInfo? Callback,
    string Summary,
    string? Doc,
    string? Default);
