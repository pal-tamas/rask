using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators;

// A value object of one value whose value has an order — a number, a day, a moment, a text — takes that order: it is
// IComparable, so `fuelings.OrderBy(f => f.Moment)` sorts a list the way the database sorts the column, and it has <,
// >, <= and >= against its own type, so `a.Moment < b.Moment` reads as it means. Value objects of two different types
// are never compared: the operators take this type alone.
public sealed partial class ValueObjectFactoryGenerator
{
    private static readonly (string Operator, string Test)[] Comparisons = [("<", "< 0"), (">", "> 0"), ("<=", "<= 0"), (">=", ">= 0")];

    // One parameter of a type that orders itself, and nothing of the record's own that what is written would clash with.
    private static bool IsOrdered(INamedTypeSymbol type, ParameterListSyntax? parameters, SemanticModel model, CancellationToken token)
    {
        if (parameters is not { Parameters.Count: 1 } ||
            model.GetDeclaredSymbol(parameters.Parameters[0], token) is not IParameterSymbol parameter ||
            parameter.Type.NullableAnnotation == NullableAnnotation.Annotated ||
            parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            !OrdersItself(parameter.Type))
        {
            return false;
        }

        return !type.AllInterfaces.Any(IsComparable) &&
               type.GetMembers("CompareTo").IsEmpty &&
               type.GetMembers("op_LessThan").IsEmpty;
    }

    private static bool IsComparable(INamedTypeSymbol type) =>
        string.Equals(type.Name, "IComparable", System.StringComparison.Ordinal) &&
        type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    private static bool OrdersItself(ITypeSymbol held) =>
        held.AllInterfaces.Any(i => i.Arity == 1 && IsComparable(i) && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], held));

    private static void AppendOrder(StringBuilder s, ValueObject value)
    {
        if (!value.Ordered)
        {
            return;
        }

        var name = value.Name;
        var (by, type) = OrderedBy(value);
        s.AppendLine();
        s.Append("    /// <summary>Orders it by the ").Append(by)
            .AppendLine(" it holds, as the database orders its column; any value is after null.</summary>");
        s.Append("    public int CompareTo(").Append(name).AppendLine("? other) =>");
        s.Append("        other is null ? 1 : global::System.Collections.Generic.Comparer<").Append(type).Append(">.Default.Compare(")
            .Append(by).Append(", other.").Append(by).AppendLine(");");
        s.AppendLine();
        s.AppendLine("    int global::System.IComparable.CompareTo(object? obj) =>");
        s.AppendLine("        obj switch");
        s.AppendLine("        {");
        s.AppendLine("            null => 1,");
        s.Append("            ").Append(name).AppendLine(" other => CompareTo(other),");
        s.Append("            _ => throw new global::System.ArgumentException(\"A ").Append(name)
            .AppendLine(" is compared with another one, not with a \" + obj.GetType().Name + \".\", nameof(obj)),");
        s.AppendLine("        };");
        foreach (var (op, test) in Comparisons)
        {
            s.AppendLine();
            s.Append("    /// <summary>Compares the ").Append(by).AppendLine(" the two hold; null is less than any value.</summary>");
            s.Append("    public static bool operator ").Append(op).Append('(').Append(name).Append("? left, ").Append(name)
                .Append("? right) => global::System.Collections.Generic.Comparer<").Append(name).Append(">.Default.Compare(left, right) ")
                .Append(test).AppendLine(";");
        }
    }

    // A moment orders by the instant it holds — the column's order, which keeps the hour the clock shows twice in
    // autumn apart — and any other value by the one it holds.
    private static (string By, string Type) OrderedBy(ValueObject value) =>
        string.Equals(value.ValueBase, "Moment", System.StringComparison.Ordinal)
            ? ("Instant", "global::System.DateTimeOffset")
            : (value.Parameters[0].Name, value.Parameters[0].Type);
}
