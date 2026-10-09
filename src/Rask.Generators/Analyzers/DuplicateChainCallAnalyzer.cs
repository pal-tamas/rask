using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK044 — a builder chain that names the same property twice.
///     <para>
///         The second write wins and the first is dead, which is never what was meant: either two people
///         edited the chain, or a copied line was not adjusted. Nothing reports it today — the chain
///         compiles, renders, and quietly uses the last value.
///     </para>
///     <para>
///         Deliberately an analyzer rather than a property of the type. Tracking which of a component's
///         setters have been used would need one state per subset — 2^n over the whole surface — where
///         the required-property machinery only pays 2^k over the few that are required.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateChainCallAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask044 = new(
        "RASK044",
        "Chain sets the same property twice",
        "This chain calls '{0}' more than once, and only the last call counts — remove whichever is stale",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A setter writes its property and hands the component back, so a chain that names one twice "
                     + "simply overwrites it. That compiles and renders, using the last value — so the mistake "
                     + "survives review and shows up as markup nobody can account for. Two writes to one property "
                     + "are always either a merge artefact or a copied line that was not adjusted.",
        helpLinkUri: DiagnosticHelp.Link("RASK044"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask044);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(ComponentSymbols.ComponentFullName);
            if (component is null)
            {
                return;
            }

            start.RegisterOperationAction(ctx => Analyze(ctx, component), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol component)
    {
        var operation = (IInvocationOperation)context.Operation;

        // Only the OUTERMOST call of a chain, so one chain is reported once rather than once per link.
        if (operation.Syntax.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return;
        }

        if (!ProducesAComponent(operation.TargetMethod.ReturnType, component))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);

        for (var current = operation; current is not null; current = Next(current))
        {
            // A step or setter takes the component and hands it back; anything else in the chain — the
            // children indexer, a cast, the entry itself — is not a write and does not count.
            if ((current.Instance is not null || current.TargetMethod.IsExtensionMethod)
                && !AddsARule(current.TargetMethod)
                && !seen.Add(current.TargetMethod.Name))
            {
                duplicates.Add(current.TargetMethod.Name);
            }
        }

        foreach (var name in duplicates)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask044, operation.Syntax.GetLocation(), name));
        }
    }

    // `.Validate(a).Validate(b)` is the one step that adds to what the chain wrote instead of replacing it:
    // a runs, then b if a let the value through. Told by what the step takes — the rule's carrier, or one of
    // the two delegate shapes the carrier holds — so a control's own Validate is covered with the built-ins.
    private static bool AddsARule(IMethodSymbol step)
    {
        if (step.Parameters.Length == 0)
        {
            return false;
        }

        var taken = step.Parameters[step.Parameters.Length - 1].Type;
        if (taken is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            taken = nullable.TypeArguments[0];
        }

        var home = taken.ContainingNamespace?.ToDisplayString();

        return taken.MetadataName switch
        {
            "Validator`1" => string.Equals(home, "Rask.Core", StringComparison.Ordinal),
            "Validate`1" => string.Equals(home, "Rask.Core.Forms", StringComparison.Ordinal),
            "Func`2" => string.Equals(step.Name, "Validate", StringComparison.Ordinal),
            _ => false,
        };
    }

    // The next link DOWN the chain: a setter's receiver is the invocation that produced it, whether it
    // was written as an extension (argument 0) or as an instance call.
    private static IInvocationOperation? Next(IInvocationOperation operation)
    {
        var receiver = operation.Instance
                       ?? (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length != 0
                           ? operation.Arguments[0].Value
                           : null);

        return Unwrap(receiver) as IInvocationOperation;
    }

    // Parentheses and a null-forgiving `!` are part of the same expression, so they pass straight through.
    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
        {
            operation = operation is IParenthesizedOperation p ? p.Operand : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }

    private static bool ProducesAComponent(ITypeSymbol? type, INamedTypeSymbol component) =>
        BuilderEntry.DerivesFromComponent(type, component);
}
