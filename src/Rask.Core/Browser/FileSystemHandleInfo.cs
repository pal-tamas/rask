namespace Rask.Core.Browser;

/// <summary>Wire shape for a picked file/entry handle — the JS-side id plus the entry name.</summary>
/// <param name="Id">The framework-minted id under which the live handle is held JS-side.</param>
/// <param name="Name">The file/entry name.</param>
public sealed record FileSystemHandleInfo(int Id, string Name);
