using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// A text the aggregate's Configure keeps empty — `b.KeepEmpty(c => c.VatNumber)` — is written as '' where the model
// leaves it empty, never as nothing: a new row starts with '', and an emptied field stores ''. Only a row that holds
// NULL there and is saved with the field still empty keeps its NULL, so a save that changes nothing writes nothing.
public sealed partial class ModelInputGenerator
{
    private const string KeepEmptyCall = "KeepEmpty";

    /// <summary>The members the aggregate's own static <c>Configure</c> names in a <c>KeepEmpty(x =&gt; x.Member)</c>.</summary>
    /// <remarks>
    ///     Read from the source, as its unique rules are, because the writes are generated before any model is built;
    ///     a call made anywhere else still marks the column, and the writes then clear an empty field as usual.
    /// </remarks>
    private static HashSet<string> KeptEmpty(INamedTypeSymbol symbol, CancellationToken cancellationToken) =>
        new(symbol.GetMembers("Configure").OfType<IMethodSymbol>()
            .Where(static method => method.IsStatic)
            .SelectMany(static method => method.DeclaringSyntaxReferences)
            .SelectMany(reference => reference.GetSyntax(cancellationToken).DescendantNodes())
            .OfType<InvocationExpressionSyntax>()
            .Select(static call => call is
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: KeepEmptyCall },
                ArgumentList.Arguments: { Count: 1 } arguments,
            } && arguments[0].Expression is LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member }
                ? member.Name.Identifier.ValueText
                : null)
            .OfType<string>(), StringComparer.Ordinal);

    // Only a text the aggregate may be without: a string, or a value object of one text.
    private static IEnumerable<Member> KeepingEmpty(IEnumerable<Member> members, HashSet<string> kept) =>
        members.Select(m => kept.Contains(m.Name) && IsOptionalText(m) ? m with { KeepsEmpty = true } : m);

    private static bool IsOptionalText(Member member) =>
        member is { Role: ModelMemberRole.Value, Nullable: true, Write: not null } &&
        member.ValueObject switch
        {
            null => string.Equals(member.ModelType, TextModelType, StringComparison.Ordinal),
            { SingleValue: true } single => string.Equals(single.Members[0].ModelType, TextModelType, StringComparison.Ordinal),
            _ => false,
        };

    // What the member holds when its field is empty: '', or the value object made from it past its rules.
    private static string EmptyValue(Member member) =>
        member.ValueObject is { } single
            ? "global::Rask.Data.GeneratedEmptyTexts.Of(static () => " + BuildExpression(single, static _ => "\"\"") + ")"
            : "\"\"";

    // A new row starts with '' in each text kept empty.
    private static void AppendEmptyTexts(StringBuilder s, Entity entity)
    {
        foreach (var member in entity.Members.Where(static m => m.KeepsEmpty))
        {
            s.Append("            ").AppendLine(Assignment(member.Write!, "entity", member.Name, EmptyValue(member)));
        }
    }

    // Filled: written as any other. Empty: '' — unless the row holds NULL there, which it keeps.
    private static void AppendKeptEmpty(StringBuilder s, Member member, string given, string value)
    {
        var filled = member.ValueObject is null ? "" : " && !string.IsNullOrWhiteSpace(" + given + ")";

        s.Append("        if (model.").Append(member.Name).Append(" is { } ").Append(given).Append(filled).AppendLine(")");
        s.AppendLine("        {");
        s.Append("            ").AppendLine(Assignment(member.Write!, "entity", member.Name, value));
        s.AppendLine("        }");
        s.AppendLine("        else");
        s.AppendLine("        {");
        s.Append("            ").AppendLine(Assignment(
            member.Write!, "entity", member.Name, "entity." + member.Name + " is null ? null : " + EmptyValue(member)));
        s.AppendLine("        }");
    }
}
