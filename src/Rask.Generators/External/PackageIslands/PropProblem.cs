using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A prop that could not be generated, and why.</summary>
internal sealed record PropProblem(string PropName, string Reason, int Line, int Column);
