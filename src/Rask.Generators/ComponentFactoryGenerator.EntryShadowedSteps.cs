using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Generators;

// An enum step named like an element — `Ui.TableColumn.Code`, `Ui.Button.Div`, `Ui.Autocomplete.Input` — is an
// extension property, and inside a markup host it never gets a look in: member lookup on the component finds the
// `protected static` entry every component inherits from RaskMarkup first, and `x.Code` on an instance is CS0176
// rather than a fall-through to the extension. Only a member of the component itself hides that entry, so those
// steps — and only those — are emitted INTO the component's partial, `new` over the inherited one.
//
// Hiding the entry hides it inside the component's own code as well: there a bare `Div` would mean the step, and
// `Div.Class("x")[…]` would compile and quietly restyle the component instead of drawing a <div>. So a step moves
// in only where nothing can be misread that way — a sealed component (no subclass inherits the hiding), whose own
// source never writes the name bare (it reaches the element as `Markup.Div`, which the hiding leaves alone).
// Anywhere else the step stays the extension it was, reachable from any non-host code, and the build says so.
public sealed partial class ComponentFactoryGenerator
{
    /// <summary>
    ///     The enum-step names the component must declare itself because an inherited element entry would hide them,
    ///     or empty when it has none or cannot take them safely (see the file comment).
    /// </summary>
    private static EquatableArray<string> StepsAnEntryShadows(
        INamedTypeSymbol symbol, ClassDeclarationSyntax classDecl, IEnumerable<PropInfo> properties, Compilation compilation)
    {
        if (!symbol.IsSealed || symbol.ContainingType is not null || !classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return default;
        }

        var names = properties
            .Where(static p => p.EnumMembers.Length > 0)
            .SelectMany(static p => p.EnumMembers.Split(','))
            .Distinct(StringComparer.Ordinal)
            .Where(name => OnlyAnInheritedStaticCarries(symbol, name, compilation))
            .ToList();
        if (names.Count == 0)
        {
            return default;
        }

        var written = BareNamesWrittenIn(symbol, names);
        return new EquatableArray<string>(names.Where(n => !written.Contains(n)).OrderBy(static n => n, StringComparer.Ordinal));
    }

    // The name is carried by a base type, accessibly from the component, and only ever as a STATIC member or a type:
    // an entry. An inherited instance member of that name (a property, a method) is the component's own surface, and
    // hiding it with a step would take it away.
    private static bool OnlyAnInheritedStaticCarries(INamedTypeSymbol symbol, string name, Compilation compilation)
    {
        var found = false;
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers(name).Where(m => !m.IsImplicitlyDeclared && compilation.IsSymbolAccessibleWithin(m, symbol)))
            {
                if (!m.IsStatic && m is not INamedTypeSymbol)
                {
                    return false;
                }

                found = true;
            }
        }

        return found;
    }

    // Every name of <paramref name="names" /> the component's own source writes as a simple name — `Div` in
    // `Div.Class("x")`, not the `Div` of `Markup.Div` or of `x.Div`. Over-counting is safe: a name counted here
    // only keeps its step an extension, as it always was.
    private static HashSet<string> BareNamesWrittenIn(INamedTypeSymbol symbol, List<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            foreach (var simple in reference.GetSyntax().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var text = simple.Identifier.ValueText;
                if (wanted.Contains(text) && !IsQualifiedRightSide(simple))
                {
                    written.Add(text);
                }
            }
        }

        return written;
    }

    private static bool IsQualifiedRightSide(SimpleNameSyntax simple) => simple.Parent switch
    {
        MemberAccessExpressionSyntax access => access.Name == simple,
        MemberBindingExpressionSyntax => true,
        QualifiedNameSyntax qualified => qualified.Right == simple,
        AliasQualifiedNameSyntax alias => alias.Name == simple,
        _ => false
    };

    /// <summary>
    ///     Each step an inherited entry would hide, as a <c>new</c> property of the component: the same body the
    ///     extension step has, a call to the property's own setter, so the pending-bits bookkeeping is unchanged.
    /// </summary>
    private static void EmitEntryShadowedSteps(
        SourceProductionContext spc, ImmutableArray<Candidate> candidates, string setterClass)
    {
        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        var wrote = false;
        foreach (var c in DistinctByType(candidates).Where(static c => c.EntryShadowedSteps.Count > 0))
        {
            var shadowed = new HashSet<string>(c.EntryShadowedSteps, StringComparer.Ordinal);
            var steps = EnumSteps(OwnSetterProps(c)).Where(s => shadowed.Contains(s.Member)).ToList();
            if (steps.Count == 0)
            {
                continue;
            }

            wrote = true;
            EmitShadowedStepsFor(sb, c, steps, setterClass);
        }

        if (wrote)
        {
            spc.AddSource("RaskEntryShadowedSteps.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    private static void EmitShadowedStepsFor(
        StringBuilder sb, Candidate c, List<(string Member, string EnumType, string Setter)> steps, string setterClass)
    {
        var visibility = c.IsPublic ? "public" : "internal";
        var hasNs = !string.IsNullOrEmpty(c.Namespace);
        sb.AppendLine();
        if (hasNs)
        {
            // Block-scoped: one file carries every component, and a file may hold only one file-scoped namespace.
            sb.Append("namespace ").AppendLine(c.Namespace);
            sb.AppendLine("{");
        }

        sb.Append("partial class ").Append(c.TypeName).Append(c.TypeParameters).AppendLine(c.TypeParameterConstraints);
        sb.AppendLine("{");
        foreach (var (member, enumType, setter) in steps)
        {
            sb.Append("    /// <summary>Sets <c>").Append(setter).Append("</c> to <c>").Append(member)
                .AppendLine("</c>.</summary>");
            // A getter that sets: the debugger must not evaluate it just to show the component.
            sb.AppendLine(
                "    [global::System.Diagnostics.DebuggerBrowsable(global::System.Diagnostics.DebuggerBrowsableState.Never)]");
            sb.Append("    ").Append(visibility).Append(" new ").Append(c.FullyQualifiedName).Append(' ')
                .Append(CSharpNames.Escape(member)).Append(" => global::").Append(setterClass).Append('.')
                .Append(CSharpNames.Escape(setter)).Append("(this, ").Append(enumType).Append('.')
                .Append(CSharpNames.Escape(member)).AppendLine(");");
        }

        sb.AppendLine("}");
        if (hasNs)
        {
            sb.AppendLine("}");
        }
    }
}
