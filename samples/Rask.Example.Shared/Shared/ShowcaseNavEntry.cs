namespace Rask.Example.Shared;

/// <summary>
///     A sidebar nav entry contributed by the host (not the shared showcase). Register instances in DI
///     and <see cref="ShowcaseLayout" /> appends them to its sidebar — this lets the WASM host surface
///     WASM-only example pages (e.g. PWA/notifications) that the shared project can't reference.
/// </summary>
public sealed record ShowcaseNavEntry(string Path, string Label, IconName Icon, string Group, string? MatchPrefix = null);
