using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK140 — domain code reads the machine's clock: <c>DateTime.Now</c>, <c>DateTime.Today</c>,
///     <c>DateTime.UtcNow</c>, <c>DateTimeOffset.Now</c>, <c>DateTimeOffset.UtcNow</c> or <c>.ToLocalTime()</c> inside
///     an aggregate, an entity or a value object.
/// </summary>
/// <remarks>
///     The machine's clock is not the app's: a test's <c>Clock.Fake(at: …)</c> does not freeze it, and its zone is the
///     server's (a container's is UTC), not the one the app named. <c>Clock.Now</c>, <c>Clock.Wall</c>,
///     <c>Clock.Today</c> and <c>Clock.Zone</c> are. A suggestion by default; a warning under <c>&lt;RaskStrict&gt;</c>.
///     Reported only when the compilation has <c>Rask.Clock</c>, so the quick-fix always has somewhere to point.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemClockAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask140 = new(
        "RASK140",
        "Domain code reads the machine's clock",
        "'{0}' in '{1}' reads the machine's clock, which a test cannot freeze and whose zone is the server's — use '{2}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An aggregate, an entity or a value object reads the time from Rask's Clock: Clock.Now (UTC), "
                     + "Clock.Wall (the wall clock in the app's zone), Clock.Today (its day) and Clock.Zone. The "
                     + "machine's DateTime.Now, Today, UtcNow, DateTimeOffset.Now and ToLocalTime() ignore a test's "
                     + "Clock.Fake and the zone the app named. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK140"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask140);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Clock") is null)
            {
                return;
            }

            start.RegisterOperationAction(AnalyzeRead, OperationKind.PropertyReference);
            start.RegisterOperationAction(AnalyzeCall, OperationKind.Invocation);
        });
    }

    private static void AnalyzeRead(OperationAnalysisContext context)
    {
        var read = (IPropertyReferenceOperation)context.Operation;
        if (read.Property is { IsStatic: true } property && Replacement(property.ContainingType, property.Name) is { } replacement)
        {
            Report(context, read.Syntax, property.ContainingType.Name + "." + property.Name, replacement);
        }
    }

    private static void AnalyzeCall(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is { Name: "ToLocalTime", IsStatic: false, Parameters.Length: 0 } method && IsSystemTime(method.ContainingType))
        {
            Report(context, call.Syntax, ".ToLocalTime()", string.Equals(method.ContainingType.Name, "DateTime", System.StringComparison.Ordinal)
                ? "TimeZoneInfo.ConvertTimeFromUtc(…, Clock.Zone)"
                : "TimeZoneInfo.ConvertTime(…, Clock.Zone)");
        }
    }

    private static void Report(OperationAnalysisContext context, SyntaxNode at, string read, string replacement)
    {
        if (DomainCode.TypeOf(context.ContainingSymbol) is { } domain)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask140, at.GetLocation(), read, domain.Name, replacement));
        }
    }

    // What the message names in place of a static read of the machine's clock, or null for any other property.
    private static string? Replacement(INamedTypeSymbol type, string name)
    {
        if (!IsSystemTime(type))
        {
            return null;
        }

        return (type.Name, name) switch
        {
            ("DateTime", "Now") => "Clock.Wall",
            ("DateTime", "Today") => "Clock.Today', or 'Clock.Wall.Date' where a DateTime is wanted",
            ("DateTime", "UtcNow") => "Clock.Now.UtcDateTime",
            ("DateTimeOffset", "Now" or "UtcNow") => "Clock.Now",
            _ => null,
        };
    }

    private static bool IsSystemTime(INamedTypeSymbol? type) =>
        type is { Name: "DateTime" or "DateTimeOffset", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
}
