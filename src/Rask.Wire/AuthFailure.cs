namespace Rask.Wire;

/// <summary>Why a request was refused.</summary>
/// <param name="Error">The <c>AuthError</c> name — the enum lives in Rask.Core, which this
/// contract deliberately does not reference, so it travels as its name.</param>
/// <param name="Message">A human-readable detail, when there is one.</param>
public sealed record AuthFailure(string Error, string? Message);
