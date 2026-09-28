using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>Whether a snapshot can be used for the island it sits beside.</summary>
internal enum PackageVerdict
{
    Usable,
    Unreadable,
    RuntimeMismatch,
    ModuleMismatch,
}
