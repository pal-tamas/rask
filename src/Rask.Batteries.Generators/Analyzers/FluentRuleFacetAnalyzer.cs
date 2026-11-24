using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK182 — a FluentValidation rule gives a member a length or precision other than the one its column takes:
///     <c>RuleFor(s =&gt; s.Name).MaximumLength(200)</c> on a <c>[MaxLength(100)]</c> property, <c>PrecisionScale(10, 2)</c>
///     on a <c>[Precision(12, 4)]</c> one, or, inside a value object, <c>MaximumLength(200)</c> on the <c>Value</c> its
///     base declares <c>Text(Value, MaxLength: 100)</c>.
/// </summary>
/// <remarks>
///     The column takes the attribute's facet, or the value object's base's, and the rule then accepts what the column
///     cannot hold, or refuses what it can. Only constants are compared, on both sides, so the warning is never a guess.
///     The FluentValidation analogue of RASK128. Silent unless the compilation references FluentValidation.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FluentRuleFacetAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask182 = new(
        "RASK182",
        "A FluentValidation rule disagrees with its column",
        "The rule gives '{0}' {1}, but {2} gives it {3} — the column takes {3} from {2}: make the two agree",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A [MaxLength], [StringLength] or [Precision] on a property, and the MaxLength, Precision and Scale a "
                     + "value object hands its Text or Number base, give the column its length and precision. A "
                     + "FluentValidation MaximumLength, Length or PrecisionScale that says otherwise leaves the rule and "
                     + "the column disagreeing: a value the rule accepts does not fit, or one that fits is refused.",
        helpLinkUri: DiagnosticHelp.Link("RASK182"));

    private static readonly string[] NumberFacets = ["Scale", "Precision"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask182);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(FluentRules.InlineValidatorName) is null)
            {
                return;
            }

            start.RegisterOperationAction(Analyze, OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var ruleFor = (IInvocationOperation)context.Operation;
        if (FluentRules.ValidatedBy(ruleFor) is not INamedTypeSymbol owner ||
            FluentRules.RuledProperty(ruleFor) is not { } member)
        {
            return;
        }

        var column = ColumnFacets(owner, member);
        if (column.Length is null && column.Precision is null)
        {
            return;
        }

        foreach (var call in FluentRules.CallsOn(ruleFor))
        {
            if (Disagreement(call, column) is not { } said)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask182, CallLocation(call), owner.Name + "." + member.Name, said.Rule, column.Source, said.Column));
        }
    }

    // What the column of the member is given, and by what: an attribute on it, or the base of the value object it is.
    private static Facets ColumnFacets(INamedTypeSymbol owner, IPropertySymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            var arguments = attribute.ConstructorArguments;
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.ComponentModel.DataAnnotations.MaxLengthAttribute" or "System.ComponentModel.DataAnnotations.StringLengthAttribute"
                    when arguments.Length > 0 && arguments[0].Value is int length:
                    return new Facets(length, null, AttributeName(attribute.AttributeClass, length.ToString(CultureInfo.InvariantCulture)));
                case "Microsoft.EntityFrameworkCore.PrecisionAttribute" when arguments.Length > 0 && arguments[0].Value is int precision:
                    var scale = arguments.Length > 1 ? arguments[1].Value as int? : null;
                    return new Facets(null, (precision, scale), AttributeName(attribute.AttributeClass, Sizes(precision, scale)));
            }
        }

        if (!string.Equals(member.Name, "Value", System.StringComparison.Ordinal) ||
            AggregateShape.ValueBaseOf(owner) is not ("Text" or "Number") ||
            ValueObjectBaseArguments.Of(owner) is not { Count: > 0 } declared)
        {
            return default;
        }

        if (ValueObjectBaseArguments.IsText(owner))
        {
            return declared.TryGetValue("MaxLength", out var maxLength) && maxLength > 0
                ? new Facets(maxLength, null, "its base Text(MaxLength: " + maxLength.ToString(CultureInfo.InvariantCulture) + ")")
                : default;
        }

        int? Declared(string name) => declared.TryGetValue(name, out var value) ? value : null;
        var facet = (Precision: Declared("Precision"), Scale: Declared("Scale"));
        return facet is (null, null) ? default : new Facets(null, facet, "its base Number(" + NumberArguments(declared) + ")");
    }

    // The rule a call states that the column does not, as the message says each; null when they agree.
    private static (string Rule, string Column)? Disagreement(IInvocationOperation call, Facets column)
    {
        if (FluentRules.IntArguments(call) is not { Count: > 0 } given)
        {
            return null;
        }

        switch (call.TargetMethod.Name)
        {
            case "MaximumLength" or "Length" when column.Length is { } length:
                var most = given[given.Count - 1];
                return most == length ? null : (LengthOf(most), LengthOf(length));
            case "PrecisionScale" when given.Count >= 2 && column.Precision is { } facet:
                return (facet.Precision is null || given[0] == facet.Precision) && (facet.Scale is null || given[1] == facet.Scale)
                    ? null
                    : (PrecisionOf(given[0], given[1]), PrecisionOf(facet.Precision, facet.Scale));
            default:
                return null;
        }
    }

    // `.MaximumLength(200)`, from the name of the call to its closing parenthesis: not the whole chain before it.
    private static Location CallLocation(IInvocationOperation call) =>
        call.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation
            ? Location.Create(invocation.SyntaxTree, TextSpan.FromBounds(access.Name.SpanStart, invocation.ArgumentList.Span.End))
            : call.Syntax.GetLocation();

    private static string AttributeName(INamedTypeSymbol attribute, string arguments) =>
        "[" + attribute.Name.Substring(0, attribute.Name.Length - nameof(System.Attribute).Length) + "(" + arguments + ")]";

    private static string NumberArguments(System.Collections.Generic.Dictionary<string, int> declared) =>
        string.Join(", ", NumberFacets
            .Where(declared.ContainsKey)
            .Select(name => name + ": " + declared[name].ToString(CultureInfo.InvariantCulture)));

    private static string Sizes(int precision, int? scale) =>
        precision.ToString(CultureInfo.InvariantCulture) + (scale is { } s ? ", " + s.ToString(CultureInfo.InvariantCulture) : "");

    private static string LengthOf(int length) => "a length of at most " + length.ToString(CultureInfo.InvariantCulture);

    private static string PrecisionOf(int? precision, int? scale) =>
        string.Join(" and ", new[] { Said("a precision of ", precision), Said("a scale of ", scale) }.Where(static s => s.Length > 0));

    private static string Said(string what, int? size) => size is { } s ? what + s.ToString(CultureInfo.InvariantCulture) : "";

    // The facets a column is given, and what gives them, as the message names it.
    private readonly record struct Facets(int? Length, (int? Precision, int? Scale)? Precision, string Source);
}
