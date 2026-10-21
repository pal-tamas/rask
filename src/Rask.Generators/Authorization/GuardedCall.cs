using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Authorization;

/// <summary>
///     One call to a component method that carries <c>[Authorize]</c>, and everything its interceptor needs to
///     check the person first and then call the method: <c>Intercepts</c> is the <c>[InterceptsLocation]</c>
///     naming the call, <c>Method</c> the key its calls share (<c>App.Page.Remove(int)</c>), and
///     <c>Unsupported</c> why it cannot be intercepted, or null when it can.
/// </summary>
internal sealed record GuardedCall(
    string? Intercepts,
    string Method,
    string ContainingType,
    string Name,
    string ReturnType,
    GuardedReturn Returns,
    bool Awaits,
    bool IsStatic,
    EquatableArray<GuardedParameter> Parameters,
    string? Unsupported,
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan)
{
    public Location Location => Microsoft.CodeAnalysis.Location.Create(FilePath, Span, LineSpan);
}
