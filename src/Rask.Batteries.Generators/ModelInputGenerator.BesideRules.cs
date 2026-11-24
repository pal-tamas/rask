using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rask.Batteries.Generators;

// A value object's own rule, live under the member a form is typing into: each member of a nested model of a value
// object that checks itself as it is made (a Range, or a Value of several members with its own Rules()) is asked what
// the value object's generated TryCreate says against it, beside the other members the model holds. Nothing is
// re-implemented here: the value object checks itself, and a problem is shown under the member it names
// (Problem.At(Quantity, …) under Opening.Quantity).
public sealed partial class ModelInputGenerator
{
    private const string Messages = "global::System.Collections.Generic.IEnumerable<string>";

    /// <summary>One member of a nested model, with the value object it belongs to and every member, in constructor order.</summary>
    private sealed record BesideMember(
        string Property, string ModelType, string ModelName, string ValueObject, IReadOnlyList<ValueObjectMember> Ordered, bool Ruled);

    // The members of a nested model whose value object checks itself, in the order its primary constructor takes
    // them; none for any other model, or for one holding a value object of its own (its rule is that one's).
    private static IEnumerable<BesideMember> BesideMembers(ValueObjectShape nested)
    {
        if (nested is not { Checked: true, SingleValue: false, ValueBase: "Range" or "Value" } ||
            nested.ConstructorOrder.Count != nested.Members.Count ||
            nested.Members.Any(static m => m.ValueObject is not null))
        {
            yield break;
        }

        var ordered = nested.ConstructorOrder
            .Select(name => nested.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<ValueObjectMember>()
            .ToList();
        if (ordered.Count != nested.Members.Count)
        {
            yield break;
        }

        foreach (var member in ordered)
        {
            yield return new BesideMember(member.Name, member.ModelType, nested.ModelName, nested.TypeName, ordered, member.RequiredByType);
        }
    }

    // The two ends of a nested range model: the members a bound control is told are a range's.
    private static IEnumerable<string> RangeEndNames(ValueObjectShape nested) =>
        nested is { ValueBase: "Range", Members.Count: 2 } ? BesideMembers(nested).Select(static m => m.Property) : [];

    // The delegate a field keeps for one member: handed the nested model it is on, so it reads the others.
    private static void AppendBesideRule(StringBuilder s, Entity entity, string modelKey, BesideMember member)
    {
        s.Append("    private static readonly global::System.Func<object, ").Append(member.ModelType).Append(", ").Append(Messages).Append("> ")
            .Append(modelKey).Append(member.Property).AppendLine(" =");
        s.Append("        ").Append(ExtensionsType(entity)).Append('.').Append(BesideMethod(member)).AppendLine(";");
    }

    private static string BesideMethod(BesideMember member) => "__Beside_" + member.ModelName + "_" + member.Property;

    private static string InValueMethod(string modelName) => "__InValue_" + modelName;

    // Beside the rule methods: per member, its own rule first (required) and then the value object's; per value object, the ask.
    private static void AppendBesideRuleMethods(StringBuilder s, Entity entity)
    {
        foreach (var nested in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            var members = BesideMembers(nested).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var holder = FormModelType(entity) + "." + nested.ModelName;
            foreach (var member in members)
            {
                AppendBesideMethod(s, holder, member);
            }

            AppendInValueMethod(s, nested, members[0].Ordered);
        }
    }

    private static void AppendBesideMethod(StringBuilder s, string holder, BesideMember member)
    {
        var asked = "(" + string.Join(", ", member.Ordered.Select(m =>
            string.Equals(m.Name, member.Property, StringComparison.Ordinal) ? "value" : "model." + m.Name)) + ", \"" + member.Property + "\")";

        s.Append("    internal static ").Append(Messages).Append(' ').Append(BesideMethod(member)).Append("(object holder, ")
            .Append(member.ModelType).AppendLine(" value)");
        s.AppendLine("    {");
        if (member.Ruled)
        {
            s.Append("        var own = ").Append(RuleMethod(member.ModelName + "_", member.Property)).AppendLine("(value);");
            s.AppendLine("        if (global::System.Linq.Enumerable.Any(own))");
            s.AppendLine("        {");
            s.AppendLine("            return own;");
            s.AppendLine("        }");
            s.AppendLine();
        }

        s.Append("        return holder is ").Append(holder).Append(" model ? ").Append(InValueMethod(member.ModelName)).Append(asked)
            .Append(" : ").Append(NoMessages).AppendLine(";");
        s.AppendLine("    }");
        s.AppendLine();
    }

    // What the value object's own TryCreate says against its members, under `member`; nothing while a member it
    // needs is empty (that one's required rule speaks for it).
    private static void AppendInValueMethod(StringBuilder s, ValueObjectShape nested, IReadOnlyList<ValueObjectMember> ordered)
    {
        static string Local(ValueObjectMember m) => "__" + m.Name;

        s.Append("    internal static ").Append(Messages).Append(' ').Append(InValueMethod(nested.ModelName)).Append('(')
            .Append(string.Join(", ", ordered.Select(static m => m.ModelType + " " + Local(m) + "Given"))).AppendLine(", string member)");
        s.AppendLine("    {");

        var needed = ordered.Where(static m => !m.Nullable).Select(m => Local(m) + "Given is not { } " + Local(m)).ToList();
        if (needed.Count > 0)
        {
            s.Append("        if (").Append(string.Join(" || ", needed)).AppendLine(")");
            s.AppendLine("        {");
            s.Append("            return ").Append(NoMessages).AppendLine(";");
            s.AppendLine("        }");
            s.AppendLine();
        }

        s.Append("        ").Append(nested.TypeName).Append(".TryCreate(")
            .Append(string.Join(", ", ordered.Select(m => m.Nullable ? Local(m) + "Given" : Local(m))))
            .AppendLine(", out _, out var __problems);");
        s.AppendLine("        return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(");
        s.AppendLine("            global::System.Linq.Enumerable.Where(__problems, p => string.Equals(p.Field, member, global::System.StringComparison.Ordinal)),");
        s.AppendLine("            static p => p.Message));");
        s.AppendLine("    }");
        s.AppendLine();
    }
}
