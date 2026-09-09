using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASKSUP001 — suppresses <b>CS0108</b> when the member it names is hiding a Rask <i>builder
///     entry</i> rather than anything the author wrote.
///     <para>
///         Every component contributes an entry named after itself, and the framework's HTML/SVG tags
///         land on <c>Rask.Core.RaskMarkup</c>, which every component inherits. So a perfectly ordinary
///         member — a <c>Component? Footer</c> property, a <c>required string Label</c>, a private
///         <c>Section(…)</c> helper, a nested <c>record Address</c> — hides an entry it never asked
///         about, and the compiler asks for a <c>new</c> that says nothing to a reader. There are ~100
///         such members in this repository alone, and a framework should not spend a keyword of its
///         users' source per accidental collision with a tag name.
///     </para>
///     <para>
///         The rule is deliberately narrow: the hidden member must itself be an entry (named after the
///         component it builds, declared on the markup surface — see <see cref="BuilderEntry" />). A
///         member that hides a REAL inherited member of a user's own base component is not an entry,
///         so CS0108 still fires there and still means what it always meant.
///     </para>
///     <para>
///         This replaces a code fix that inserted the <c>new</c> instead. That fix existed because a
///         suppressor was believed unworkable: <c>dotnet format</c> does not honour suppressions, so it
///         would re-apply the fix on every run and the format gate would never settle. Removing the fix
///         provider removes the thing being re-applied — with no registered fixer for CS0108, the
///         formatter has nothing to write, and the gate settles. See the tests.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HiddenBuilderEntrySuppressor : DiagnosticSuppressor
{
    private const string HidesInheritedMember = "CS0108";

    private static readonly SuppressionDescriptor RaskSup001 = new(
        "RASKSUP001",
        HidesInheritedMember,
        "The hidden member is a generated Rask builder entry named after a component or HTML tag, "
        + "not a member of the author's own base type.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        ImmutableArray.Create(RaskSup001);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (HidesABuilderEntry(context, diagnostic))
            {
                context.ReportSuppression(Suppression.Create(RaskSup001, diagnostic));
            }
        }
    }

    private static bool HidesABuilderEntry(SuppressionAnalysisContext context, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
        {
            return false;
        }

        var model = context.GetSemanticModel(tree);
        if (model.Compilation.GetTypeByMetadataName(BuilderEntry.ComponentFullName) is not { } component)
        {
            return false;
        }

        var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
        if (model.GetDeclaredSymbol(node, context.CancellationToken) is not { } hiding)
        {
            return false;
        }

        // What CS0108 is about is the member of a BASE type that shares this name, so start one type up.
        // A same-type collision is a duplicate-definition error, never this warning.
        for (var current = hiding.ContainingType?.BaseType; current is not null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMembers(hiding.Name))
            {
                if (BuilderEntry.EntryTypeOf(candidate, component) is not null
                    || IsSeedEntry(candidate, component))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     The other entry shape: a GENERIC component cannot be opened by a plain
    ///     <c>Build&lt;T&gt;</c> member, because the type argument is not known until the chain names
    ///     it, so the generator emits a seed field typed <c>RaskSeed_{Component}</c> that carries
    ///     <c>Of&lt;T&gt;()</c> and the inferring opening steps instead.
    /// </summary>
    /// <remarks>
    ///     <see cref="BuilderEntry.EntryTypeOf" /> answers only for the property/method shape, and is
    ///     shared with analyzers that reason about a chain's component — which a seed does not name.
    ///     Widening it there would change what those analyzers see, so the seed is recognised here.
    ///     Without this, <c>Form</c>, <c>Input</c>, <c>Select</c> and every other generic component
    ///     kept warning: nine of them inside Rask.Core alone.
    /// </remarks>
    private static bool IsSeedEntry(ISymbol candidate, INamedTypeSymbol component)
    {
        if (!BuilderEntry.IsEntryHost(candidate.ContainingType, component))
        {
            return false;
        }

        var type = candidate switch
        {
            IFieldSymbol f => f.Type,
            IPropertySymbol { IsIndexer: false } p => p.Type,
            IMethodSymbol { MethodKind: MethodKind.Ordinary } m => m.ReturnType,
            _ => null,
        };

        return type is not null
               && string.Equals(type.Name, "RaskSeed_" + candidate.Name, StringComparison.Ordinal);
    }
}
