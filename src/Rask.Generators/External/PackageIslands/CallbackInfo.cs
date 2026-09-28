using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>How a callback prop's argument crosses back into C#.</summary>
/// <param name="ArgIndex">The position of the forwarded argument, or -1 when none is forwarded.</param>
/// <param name="ArgType">The forwarded argument's C# type, or null for an argless callback.</param>
/// <param name="ArgNullable">Whether the forwarded argument may be null.</param>
internal sealed record CallbackInfo(int ArgIndex, CsType? ArgType, bool ArgNullable);
