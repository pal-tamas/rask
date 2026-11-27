using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK195 — EF Core is asked to load a <c>BelongsTo</c> with the aggregate that holds it:
///     <c>db.Fuelings.Include(f =&gt; f.Vehicle)</c>, a <c>ThenInclude</c> onto one, or <c>Entry(f).Reference(f =&gt; f.Vehicle)</c>.
/// </summary>
/// <remarks>
///     A <c>BelongsTo</c> is no navigation of the write model: the other aggregate is never loaded, tracked or saved as part
///     of this one, which is the whole of the border. EF Core would refuse the call when it runs; this says so where it is
///     written, and where to read the other aggregate instead — its read face, through the query's <c>Include</c> or the
///     reference's <c>Read()</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BelongsToLoadAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask195 = new(
        "RASK195",
        "A BelongsTo is loaded with the aggregate that holds it",
        "'{0}' loads '{1}.{2}' with '{1}', but a BelongsTo is another aggregate's, never loaded as part of this one — read it "
        + "through its read face: '{1}.Include({3} => {3}.{2})', or 'await {3}.{2}.Read()'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A BelongsTo<T> holds only T's id. EF Core maps the id and no navigation, so an Include, a ThenInclude or "
                     + "an Entry(…).Reference(…) over it fails when it runs — and would load the other aggregate into this one's "
                     + "context, where a save could change it. Read it from its read face: the query's Include, which is the "
                     + "read face's navigation of the same name, or the reference's Read().",
        helpLinkUri: DiagnosticHelp.Link("RASK195"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask195);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(BelongsToShape.MetadataName) is not null)
            {
                start.RegisterOperationAction(Analyze, OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod;
        if (method.Name is not ("Include" or "ThenInclude" or "Reference") ||
            method.TypeArguments.Length == 0 ||
            !IsEntityFramework(method.ContainingType) ||
            BelongsToShape.TargetOf(method.TypeArguments[method.TypeArguments.Length - 1]) is null ||
            Navigation(call) is not { } navigation)
        {
            return;
        }

        var holder = navigation.Property.ContainingType.Name;
        context.ReportDiagnostic(Diagnostic.Create(
            Rask195,
            call.Syntax.GetLocation(),
            method.Name,
            holder,
            navigation.Property.Name,
            char.ToLowerInvariant(holder[0]).ToString()));
    }

    // The property the lambda reaches: `f => f.Vehicle`.
    private static IPropertyReferenceOperation? Navigation(IInvocationOperation call) =>
        call.Arguments.Select(static a => Lambda(a.Value)).FirstOrDefault(static l => l is not null) is { } lambda
            ? Returned(lambda)
            : null;

    private static IAnonymousFunctionOperation? Lambda(IOperation value) => value switch
    {
        IAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda,
        IConversionOperation conversion => Lambda(conversion.Operand),
        _ => null,
    };

    private static IPropertyReferenceOperation? Returned(IAnonymousFunctionOperation lambda) =>
        lambda.Body.Operations.Length == 1 && lambda.Body.Operations[0] is IReturnOperation { ReturnedValue: { } value }
            ? Unwrapped(value) as IPropertyReferenceOperation
            : null;

    private static IOperation Unwrapped(IOperation value) =>
        value is IConversionOperation { IsImplicit: true } conversion ? Unwrapped(conversion.Operand) : value;

    private static bool IsEntityFramework(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() is { } name &&
        (string.Equals(name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
         name.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal));
}
