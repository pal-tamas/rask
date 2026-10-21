using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Authorization;

/// <summary>
///     Holds every call to a component method that carries <c>[Authorize]</c> to that method's rule.
/// </summary>
/// <remarks>
///     <para>
///         A method group handed to a callback — <c>.OnClick(Remove)</c> — is checked at run time, from the
///         delegate's method. A lambda is not: <c>.OnClick(() =&gt; Remove())</c> hands over a method the compiler
///         made, which carries no attribute. So the call inside it is intercepted where it is written, and checks
///         the person before <c>Remove</c> runs. Every direct call is, from any method of the project, so the rule
///         belongs to the method and not to how it was reached.
///     </para>
///     <para>
///         A call that cannot be intercepted — a generic method, a call through <c>base</c> — is RASK109.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class HandlerAuthorizationGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Rask109 = new(
        "RASK109",
        "A call to an [Authorize] method cannot be checked where it is written",
        "The call to '{0}' is not checked against its [Authorize] because {1} — hand the method to the callback as a "
        + "method group, or check the person before calling it",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "A component method with [Authorize] is checked wherever it is called: a method group handed to a callback "
        + "at run time, and every direct call — including one inside a lambda — by an interceptor the build writes. "
        + "An interceptor cannot be written for a generic method, a call through base, a method whose signature names "
        + "a private type, or a call inside an expression tree, so such a call runs the method unchecked.",
        DiagnosticHelp.Link("RASK109"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var calls = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (syntax, cancellation) => GuardedCalls.Of(syntax, cancellation))
            .Where(static call => call is not null)
            .Collect();

        context.RegisterSourceOutput(calls, static (spc, found) => Emit(spc, found!));
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<GuardedCall> found)
    {
        foreach (var call in found.Where(call => call.Unsupported is not null))
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask109, call.Location, call.Method, call.Unsupported));
        }

        var methods = found
            .Where(call => call.Unsupported is null)
            .GroupBy(call => call.Method, System.StringComparer.Ordinal)
            .OrderBy(method => method.Key, System.StringComparer.Ordinal)
            .ToList();
        if (methods.Count == 0)
        {
            return;
        }

        spc.AddSource("__RaskGuardedHandlers.g.cs", SourceText.From(GuardedCallEmitter.Emit(methods), Encoding.UTF8));
    }
}
