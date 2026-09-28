using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>What a package declaration's <c>Module</c> and <c>Exports</c> say, read out of its syntax.</summary>
/// <param name="Runtime">The runtime its base class names.</param>
/// <param name="Module">The package, or null when it could not be read.</param>
/// <param name="Islands">One island per export, in the order written.</param>
/// <param name="Failed">The member that is not a constant (<c>Module</c> or <c>Exports</c>), for RASK059, or null.</param>
/// <param name="FailedAt">Where that member is.</param>
internal sealed record PackageDeclaration(
    string Runtime,
    string? Module,
    IReadOnlyList<PackageExportIsland> Islands,
    string? Failed,
    Location? FailedAt);
