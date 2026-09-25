using Microsoft.CodeAnalysis;

namespace Rask.Api.Generators;

/// <summary>
///     One <c>app.MapGet(...)</c>-style registration, reduced to what the generator can read from it.
/// </summary>
/// <param name="Verb">The HTTP method the Map call names.</param>
/// <param name="Pattern">The route pattern, which must be a compile-time constant.</param>
/// <param name="Handler">The lambda or method group that answers it.</param>
/// <param name="Name">The name from a chained <c>.WithName("…")</c>, when there is one.</param>
/// <param name="Site">Where to report a diagnostic about it.</param>
internal sealed record MinimalApiRegistration(
    string Verb,
    string Pattern,
    IMethodSymbol Handler,
    string? Name,
    Location Site);
