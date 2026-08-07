using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK025 — warn when an explicit string-only InputType is set on an Input<T> whose T is not string.
// A generic Input<T> derives its HTML input type from T (bool→checkbox, int/decimal→number, DateOnly→date,
// …). The string-only InputTypes — Text/Search/Tel/Url/Email/Password — only make sense for Input<string>;
// pairing one with Input<int>/Input<bool>/… is a mistake (the value would never round-trip). The fix is to
// drop the explicit Type (it's inferred from T) or bind a string. Suppressible like any analyzer.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InputTypeMismatchAnalyzer : DiagnosticAnalyzer
{
    private const string InputMetadataName = "Rask.Core.Components.Input`1";
    private const string RaskCoreAssembly = "Rask.Core";
    private const string GeneratedClassName = "Generated";
    private const string BuilderSettersPrefix = "RaskBuilderSetters";
    private const string TypeParameter = "Type";

    // The InputType members whose value is a string (text family). Mirrors InputType.cs.
    private static readonly HashSet<string> StringFamily = new(StringComparer.Ordinal)
    {
        "Text", "Search", "Tel", "Url", "Email", "Password"
    };

    private static readonly DiagnosticDescriptor Rask025 = new(
        "RASK025",
        "Input type conflicts with the bound value type",
        "Input<{0}> derives its HTML type from {0}; the string-only InputType.{1} can't apply — drop Type "
        + "(it is inferred from {0}) or bind a string",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "A non-string Input<T> (e.g. Input<int>, Input<bool>, Input<DateOnly>) renders a type derived from "
        + "T (number, checkbox, date, …). The string-only InputTypes (Text/Search/Tel/Url/Email/Password) "
        + "only apply to Input<string>.",
        DiagnosticHelp.Link("RASK025"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask025);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            var inputOpen = start.Compilation.GetTypeByMetadataName(InputMetadataName);
            if (inputOpen is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, inputOpen),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol inputOpen)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        // Two surfaces carry the same mistake. The factory passes Type as an argument to
        // `Generated.Input<T>(…)`; the builder chains a `.Type(…)` setter onto an `Input<T>` receiver
        // (`Input(() => m.Flag).Type(InputType.Text)`). Both resolve to the same question — is this
        // Input's T a string? — so both are checked here rather than in two analyzers.
        INamedTypeSymbol? control;
        ExpressionSyntax? typeArg;
        if (IsBuilderSetter(method) && string.Equals(method.Name, TypeParameter, StringComparison.Ordinal))
        {
            control = method.ReceiverType as INamedTypeSymbol;
            typeArg = invocation.ArgumentList.Arguments.Count == 1
                ? invocation.ArgumentList.Arguments[0].Expression
                : null;
        }
        else if (method.IsStatic
                 && string.Equals(method.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal)
                 && string.Equals(method.Name, "Input", StringComparison.Ordinal))
        {
            control = method.ReturnType as INamedTypeSymbol;
            typeArg = FindTypeArgument(invocation, method);
        }
        else
        {
            return;
        }

        if (control is not { TypeArguments.Length: 1 }
            || !SymbolEqualityComparer.Default.Equals(control.OriginalDefinition, inputOpen))
        {
            return;
        }

        // string T is the one case where the string-only InputTypes are valid.
        var valueType = control.TypeArguments[0];
        if (valueType.SpecialType == SpecialType.System_String)
        {
            return;
        }

        if (typeArg is null)
        {
            return; // No explicit Type → derived from T, nothing to flag.
        }

        // Only flag a statically-known InputType member from the string family.
        if (context.SemanticModel.GetSymbolInfo(typeArg, context.CancellationToken).Symbol is IFieldSymbol member
            && StringFamily.Contains(member.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask025, typeArg.GetLocation(),
                valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), member.Name));
        }
    }

    // A builder-surface setter: a generated extension method on the global-namespace
    // RaskBuilderSetters{Assembly} class. SemanticModel hands back the REDUCED symbol for `x.Type(v)`,
    // whose IsStatic is false and whose ReceiverType is the component — which is exactly what we want.
    private static bool IsBuilderSetter(IMethodSymbol method) =>
        method.MethodKind == MethodKind.ReducedExtension
        && method.ContainingType?.Name.StartsWith(BuilderSettersPrefix, StringComparison.Ordinal) == true;

    // The explicit Type argument expression — passed by name (Type: …) or positionally (its parameter
    // index within the leading positional arguments). Null when Type isn't supplied.
    private static ExpressionSyntax? FindTypeArgument(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var typeIndex = -1;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (string.Equals(method.Parameters[i].Name, TypeParameter, StringComparison.Ordinal))
            {
                typeIndex = i;
                break;
            }
        }

        if (typeIndex < 0)
        {
            return null;
        }

        var positional = 0;
        var sawNamed = false;
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon is { } name)
            {
                sawNamed = true;
                if (string.Equals(name.Name.Identifier.ValueText, TypeParameter, StringComparison.Ordinal))
                {
                    return arg.Expression;
                }
            }
            else if (!sawNamed)
            {
                if (positional == typeIndex)
                {
                    return arg.Expression;
                }

                positional++;
            }
        }

        return null;
    }
}
