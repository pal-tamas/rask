using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK128 — the EF configuration gives a column a length or precision other than the one its value object
///     declares: <c>builder.Property(x =&gt; x.Name).HasMaxLength(200)</c> on a <c>Text(MaxLength: 1000)</c>.
/// </summary>
/// <remarks>
///     Rask gives the column the value object's own length and precision; a call in <c>Configure</c> still wins,
///     because it runs after, so the column and the rule then disagree: a name the rule accepts does not fit. Only
///     literal arguments are compared, on both sides — the value object's base arguments and the call's — so the
///     warning is never a guess.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueObjectColumnFacetAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask128 = new(
        "RASK128",
        "A column is configured differently from its value object",
        "'{0}' holds a '{1}', which declares {2}, but '{3}' gives its column {4} — change the value object, or drop the call",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A value object declared from Text or Number gives its column the length, precision and scale it "
                     + "declares. A HasMaxLength, HasPrecision or HasColumnType in Configure overrides that, and the "
                     + "column then holds less, or other, than the rule accepts.",
        helpLinkUri: DiagnosticHelp.Link("RASK128"));

    // The parameters of each base's constructor, in order, for an argument given by position.
    private static readonly string[] TextParameters = ["Value", "MaxLength", "MinLength", "Pattern", "Message", "Trim"];
    private static readonly string[] NumberParameters = ["Value", "Min", "Max", "Scale", "Precision"];
    private static readonly string[] NumberFacets = ["Precision", "Scale"];

    private static readonly Regex Sizes = new(@"\(\s*(?<first>\d+|max)\s*(?:,\s*(?<second>\d+)\s*)?\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, System.TimeSpan.FromSeconds(1));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask128);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod.Name is not ("HasMaxLength" or "HasPrecision" or "HasColumnType") ||
            Receiver(call) is not IInvocationOperation { TargetMethod.Name: "Property" } property ||
            Configured(property) is not { } configured ||
            configured.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is not INamedTypeSymbol valueObject ||
            AggregateShape.ValueBaseOf(valueObject) is not ("Text" or "Number") ||
            Declared(valueObject) is not { Count: > 0 } declared)
        {
            return;
        }

        var text = IsText(valueObject);
        var given = Given(call);
        if (given is null || Disagrees(text, declared, call.TargetMethod.Name, given.Value) is not { } said)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask128, call.Syntax.GetLocation(), configured.Name, valueObject.Name, Describe(text, declared), call.TargetMethod.Name, said));
    }

    // What the call is made on: its instance, or the first argument of an extension (HasColumnType is relational's).
    private static IOperation? Receiver(IInvocationOperation call) =>
        call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0 ? Unconverted(call.Arguments[0].Value) : call.Instance;

    private static IOperation Unconverted(IOperation operation) =>
        operation is IConversionOperation { IsImplicit: true } conversion ? Unconverted(conversion.Operand) : operation;

    // The property `Property(x => x.Name)` names.
    private static IPropertySymbol? Configured(IInvocationOperation property)
    {
        if (property.Arguments.Length != 1 ||
            property.Arguments[0].Syntax is not ArgumentSyntax { Expression: LambdaExpressionSyntax { Body: ExpressionSyntax body } } ||
            property.SemanticModel is not { } model)
        {
            return null;
        }

        return model.GetSymbolInfo(body).Symbol as IPropertySymbol;
    }

    // The literal arguments the value object hands its base, by parameter name.
    private static Dictionary<string, int>? Declared(INamedTypeSymbol valueObject)
    {
        var names = IsText(valueObject) ? TextParameters : NumberParameters;
        var arguments = valueObject.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<RecordDeclarationSyntax>()
            .SelectMany(static r => r.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>() ?? [])
            .Select(static b => b.ArgumentList)
            .FirstOrDefault();

        if (arguments is null)
        {
            return null;
        }

        var declared = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (var i = 0; i < arguments.Arguments.Count; i++)
        {
            var argument = arguments.Arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText ?? (i < names.Length ? names[i] : null);
            if (name is not null && argument.Expression is LiteralExpressionSyntax { Token.Value: int value })
            {
                declared[name] = value;
            }
        }

        return declared;
    }

    // The literal sizes the call gives: (length or precision, scale); null when they are not literals.
    private static (int? First, int? Second)? Given(IInvocationOperation call)
    {
        var literals = call.Arguments.Skip(call.TargetMethod.IsExtensionMethod ? 1 : 0).Select(static a => a.Value.ConstantValue).ToList();
        if (literals.Count == 0 || literals.Exists(static c => !c.HasValue))
        {
            return null;
        }

        if (literals[0].Value is string columnType)
        {
            var match = Sizes.Match(columnType);
            if (!match.Success)
            {
                return (null, null);
            }

            return (int.TryParse(match.Groups["first"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : -1,
                match.Groups["second"].Success ? int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture) : null);
        }

        return (literals[0].Value as int?, literals.Count > 1 ? literals[1].Value as int? : null);
    }

    // What the call says that the value object does not, or null when they agree.
    private static string? Disagrees(bool text, Dictionary<string, int> declared, string method, (int? First, int? Second) given)
    {
        if (text)
        {
            if (!declared.TryGetValue("MaxLength", out var length) || length == 0 || given.First is not { } size ||
                size == length || string.Equals(method, "HasPrecision", System.StringComparison.Ordinal))
            {
                return null;
            }

            return size < 0 ? "no length" : "a length of " + size.ToString(CultureInfo.InvariantCulture);
        }

        if (string.Equals(method, "HasMaxLength", System.StringComparison.Ordinal))
        {
            return null;
        }

        var precisionDiffers = declared.TryGetValue("Precision", out var precision) && given.First is { } p && p != precision;
        var scaleDiffers = declared.TryGetValue("Scale", out var scale) && given.Second is { } s && s != scale;
        return precisionDiffers || scaleDiffers
            ? "a precision of " + Written(given.First) + " and a scale of " + Written(given.Second)
            : null;
    }

    private static string Describe(bool text, Dictionary<string, int> declared)
    {
        if (text)
        {
            return declared.TryGetValue("MaxLength", out var length) ? "MaxLength: " + length.ToString(CultureInfo.InvariantCulture) : "no length";
        }

        return string.Join(", ", NumberFacets
            .Where(declared.ContainsKey)
            .Select(name => name + ": " + declared[name].ToString(CultureInfo.InvariantCulture)));
    }

    private static bool IsText(INamedTypeSymbol valueObject) =>
        string.Equals(AggregateShape.ValueBaseOf(valueObject), "Text", System.StringComparison.Ordinal);

    private static string Written(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "none";
}
