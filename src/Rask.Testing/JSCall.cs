namespace Rask.Testing;

/// <summary>One recorded JS interop call: the dotted <paramref name="Identifier" /> and its arguments.</summary>
/// <param name="Identifier">The identifier the component invoked, e.g. <c>"raskApi.clipboard.write"</c>.</param>
/// <param name="Args">The arguments passed, or <c>null</c> if none.</param>
public readonly record struct JSCall(string Identifier, object?[]? Args);
