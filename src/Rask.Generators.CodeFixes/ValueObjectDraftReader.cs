using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Generators.CodeFixes;

// Reads the value object RASK130's fix declares for a bare property from the rules already written for it: the
// [StringLength]/[MaxLength]/[MinLength]/[Range] on it, and the RuleFor chains of its type's in-type FluentValidation
// Rules. Each rule becomes the base record's argument that says the same, and is taken off the property.
//
// Null — no fix — whenever the rewrite would not mean what was written: a type no base record holds, a name already
// taken, two rules that disagree, a conditional (When/Unless) rule, a DataAnnotations rule no base argument carries
// (left on a value object it would silently refuse every value), or a rule written in another file.
internal static class ValueObjectDraftReader
{
    private const string ValuesNamespace = "Rask.Values";
    private const string DataAnnotations = "System.ComponentModel.DataAnnotations.";

    // Calls that phrase the check before them: taken off with it.
    private static readonly HashSet<string> MessageCalls = new(StringComparer.Ordinal) { "WithMessage", "WithErrorCode", "WithSeverity", "WithState" };

    // Calls that shape the whole rule and check nothing: they stay, and a rule left with only these goes.
    private static readonly HashSet<string> RuleCalls = new(StringComparer.Ordinal) { "WithName", "OverridePropertyName", "Cascade" };

    private static readonly HashSet<string> ConditionCalls = new(StringComparer.Ordinal) { "When", "Unless", "WhenAsync", "UnlessAsync", "DependentRules" };

    public static ValueObjectDraft? Read(SemanticModel model, PropertyDeclarationSyntax node, CancellationToken cancellationToken)
    {
        if (model.GetDeclaredSymbol(node, cancellationToken) is not IPropertySymbol { ContainingType: { } owner } property ||
            ValueBase.Of(Held(property.Type, out var nullable)) is not { } valueBase ||
            model.Compilation.GetTypeByMetadataName(ValuesNamespace + "." + valueBase.Name) is null)
        {
            return null;
        }

        var name = owner.Name + property.Name;
        if (model.LookupNamespacesAndTypes(node.SpanStart, name: name).Any())
        {
            return null;
        }

        // A rule about the property anywhere but its type's own Rules — an AbstractValidator<T> class — would not compile
        // once the property holds a value object, and this fix does not rewrite it: no fix rather than a broken build.
        if (RuledElsewhere(model.Compilation, property, cancellationToken))
        {
            return null;
        }

        var arguments = new BaseArguments();
        var attributes = ReadAttributes(property, node.SyntaxTree, valueBase, arguments, cancellationToken);
        var chains = attributes is null ? null : ReadRules(model, property, node.SyntaxTree, valueBase, nullable, arguments, cancellationToken);
        if (attributes is null || chains is null || arguments.Conflicts)
        {
            return null;
        }

        if (valueBase.IsText && !nullable && !arguments.Required && !(arguments.Number("MinLength") >= 1))
        {
            arguments.Set("Required", "false");
        }

        var (baseName, needsUsing) = BaseName(model, node.SpanStart, valueBase.Name);
        var held = Held(property.Type, out _).ToMinimalDisplayString(model, node.SpanStart);
        var declaration = "public sealed partial record " + name + "(" + held + " Value) : " + baseName + "(Value" +
                          string.Concat(valueBase.Parameters.Where(arguments.Has).Select(p => ", " + p + ": " + arguments[p])) + ");";

        return new ValueObjectDraft(name, declaration, nullable, needsUsing, attributes, chains);
    }

    // The value a property holds — `int` for `int?` — and whether it may be left empty.
    private static ITypeSymbol Held(ITypeSymbol type, out bool nullable)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional)
        {
            nullable = true;
            return optional.TypeArguments[0];
        }

        nullable = type.NullableAnnotation == NullableAnnotation.Annotated;
        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    }

    // `Text`, where it binds to Rask's; `Rask.Values.Text` where another Text is in scope; `Text` and a using where none is.
    private static (string Name, bool NeedsUsing) BaseName(SemanticModel model, int position, string name)
    {
        if (UsingDirectives.Binds(model, position, name, ValuesNamespace + "." + name))
        {
            return (name, false);
        }

        return model.LookupNamespacesAndTypes(position, name: name).Any()
            ? (ValuesNamespace + "." + name, false)
            : (name, true);
    }

    // The attributes the base record's arguments take over; null when a validation attribute stays that no base
    // argument carries — on a value object it would refuse every value.
    private static List<AttributeSyntax>? ReadAttributes(
        IPropertySymbol property, SyntaxTree tree, ValueBase valueBase, BaseArguments arguments, CancellationToken cancellationToken)
    {
        var redundant = new List<AttributeSyntax>();
        foreach (var attribute in property.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString() ?? "";
            if (string.Equals(name, DataAnnotations + "RequiredAttribute", StringComparison.Ordinal))
            {
                arguments.Required = true;
                continue;
            }

            if (!name.StartsWith(DataAnnotations, StringComparison.Ordinal) || !IsValidation(attribute.AttributeClass))
            {
                continue;
            }

            if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax ||
                syntax.SyntaxTree != tree ||
                !AttributeArguments.Read(attribute, name.Substring(DataAnnotations.Length), valueBase, arguments))
            {
                return null;
            }

            redundant.Add(syntax);
        }

        return redundant;
    }

    private static bool IsValidation(INamedTypeSymbol? attribute)
    {
        for (var current = attribute; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), DataAnnotations + "ValidationAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a RuleFor outside the type's in-type Rules names the property.
    private static bool RuledElsewhere(Compilation compilation, IPropertySymbol property, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!tree.GetText(cancellationToken).ToString().Contains("RuleFor", StringComparison.Ordinal))
            {
                continue;
            }

            var treeModel = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (treeModel.GetEnclosingSymbol(invocation.SpanStart, cancellationToken) is IMethodSymbol enclosing &&
                    FluentRules.IsInlineRules(enclosing) &&
                    SymbolEqualityComparer.Default.Equals(enclosing.ContainingType, property.ContainingType))
                {
                    continue;
                }

                if (treeModel.GetOperation(invocation, cancellationToken) is IInvocationOperation ruleFor &&
                    FluentRules.ValidatedBy(ruleFor) is not null &&
                    SymbolEqualityComparer.Default.Equals(FluentRules.RuledProperty(ruleFor), property))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Every RuleFor naming the property in its type's in-type Rules, with the calls each keeps.
    private static List<FluentChainEdit>? ReadRules(
        SemanticModel model, IPropertySymbol property, SyntaxTree tree, ValueBase valueBase, bool nullable,
        BaseArguments arguments, CancellationToken cancellationToken)
    {
        var chains = new List<FluentChainEdit>();
        var rules = property.ContainingType.GetMembers(FluentRules.RulesMethod).OfType<IMethodSymbol>().Where(FluentRules.IsInlineRules);
        foreach (var reference in rules.SelectMany(static m => m.DeclaringSyntaxReferences))
        {
            var declared = reference.GetSyntax(cancellationToken);
            var treeModel = declared.SyntaxTree == model.SyntaxTree ? model : model.Compilation.GetSemanticModel(declared.SyntaxTree);
            foreach (var invocation in declared.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (treeModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation ruleFor ||
                    FluentRules.ValidatedBy(ruleFor) is null ||
                    !SymbolEqualityComparer.Default.Equals(FluentRules.RuledProperty(ruleFor), property))
                {
                    continue;
                }

                if (declared.SyntaxTree != tree || Chain(ruleFor, invocation, valueBase, nullable, arguments) is not { } chain)
                {
                    return null;
                }

                chains.Add(chain);
            }
        }

        return chains;
    }

    // One RuleFor's calls: each check a base argument says is taken off, with the message phrasing it.
    private static FluentChainEdit? Chain(
        IInvocationOperation ruleFor, InvocationExpressionSyntax syntax, ValueBase valueBase, bool nullable, BaseArguments arguments)
    {
        var calls = FluentRules.CallsOn(ruleFor);
        if (calls.Any(static c => ConditionCalls.Contains(c.TargetMethod.Name)) ||
            calls.Any(static c => c.Syntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax }))
        {
            return null;
        }

        var kept = new List<InvocationExpressionSyntax>();
        var checks = 0;
        var tookLast = false;
        foreach (var call in calls)
        {
            var name = call.TargetMethod.Name;
            var callSyntax = (InvocationExpressionSyntax)call.Syntax;
            if (MessageCalls.Contains(name))
            {
                if (!tookLast)
                {
                    kept.Add(callSyntax);
                }

                continue;
            }

            tookLast = !RuleCalls.Contains(name) && FluentArguments.Read(call, valueBase, nullable, arguments);
            if (!tookLast)
            {
                kept.Add(callSyntax);
                checks += RuleCalls.Contains(name) ? 0 : 1;
            }
        }

        var outermost = calls.Count > 0 ? (ExpressionSyntax)calls[calls.Count - 1].Syntax : syntax;
        return new FluentChainEdit(syntax, outermost, kept, checks == 0);
    }
}
