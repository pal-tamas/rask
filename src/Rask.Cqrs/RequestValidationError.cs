namespace Rask.Cqrs;

/// <summary>
///     One thing wrong with a request: the field it is about, and what to say about it.
/// </summary>
/// <param name="Field">
///     The request property the failure belongs to. Empty for a rule about the request as a whole.
/// </param>
/// <param name="Message">The message, written for whoever sent the request.</param>
public readonly record struct RequestValidationError(string Field, string Message);
