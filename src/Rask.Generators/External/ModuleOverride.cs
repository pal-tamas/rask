using Microsoft.CodeAnalysis;

namespace Rask.Generators.External;

/// <summary>What an island's <c>Module</c> (or <c>Export</c>) override says, read out of its syntax.</summary>
/// <param name="Value">The literal, when one could be read.</param>
/// <param name="Declared">Whether the class overrides the property at all.</param>
/// <param name="Failed">Whether it overrides it with something that is not a constant string (RASK059).</param>
/// <param name="Location">Where the override is, for diagnostics.</param>
internal readonly record struct ModuleOverride(string? Value, bool Declared, bool Failed, Location? Location);
