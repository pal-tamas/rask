using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK131 — a property names itself with <c>[DisplayName]</c>, which Rask never reads.
/// </summary>
/// <remarks>
///     Rask has one way to name a field for its readers — a control's label, a filter's chip, a column's header,
///     the <c>{0}</c> of a validation message, the messages a generated check writes on the server — and it is
///     <c>[Display(Name = …)]</c>, the attribute .NET's <c>Validator</c> reads too, with its <c>ResourceType</c>
///     for a translated name. <c>System.ComponentModel.DisplayNameAttribute</c> is read nowhere, so a member
///     carrying only that one is labelled by its own name, silently. A subclass of it (a hand-rolled localized
///     one) is just as unread, so it is reported as well. <c>[DisplayName]</c> cannot sit on a field, so only a
///     property is asked.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisplayNameAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string DisplayNameAttribute = "System.ComponentModel.DisplayNameAttribute";

    private static readonly DiagnosticDescriptor Rask131 = new(
        "RASK131",
        "[DisplayName] is not read by Rask",
        "'{0}' is named by [DisplayName], which no label, filter, column or validation message reads — "
        + "Rask reads [Display], not [DisplayName]: write [Display(Name = …)]",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Rask names a field for its readers by [Display(Name = …)] from "
                     + "System.ComponentModel.DataAnnotations alone, through its ResourceType where it names one: "
                     + "the control's label, a filter chip, a column header, the {0} of a validation message and "
                     + "the server's generated check messages all read it. System.ComponentModel.DisplayNameAttribute "
                     + "is read by none of them, so the field shows its member name instead. The quick-fix rewrites "
                     + "[DisplayName(\"X\")] as [Display(Name = \"X\")].",
        helpLinkUri: DiagnosticHelp.Link("RASK131"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask131);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Property);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (!IsDisplayName(attribute.AttributeClass)
                || attribute.ApplicationSyntaxReference is not { } applied)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask131, applied.GetSyntax(context.CancellationToken).GetLocation(), context.Symbol.Name));
        }
    }

    private static bool IsDisplayName(INamedTypeSymbol? attribute)
    {
        for (var type = attribute; type is not null; type = type.BaseType)
        {
            if (string.Equals(type.ToDisplayString(), DisplayNameAttribute, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
