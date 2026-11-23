using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rask.Batteries.Generators;

// A range's rule, live under the end a form is typing into: each end of a nested range model (a Rask.Data Range, two
// members) is asked what the range's own generated TryCreate says against it, beside the other end the model holds.
// Nothing is re-implemented here: the range checks itself, and a problem is shown under the member it names.
public sealed partial class ModelInputGenerator
{
    private const string Messages = "global::System.Collections.Generic.IEnumerable<string>";

    /// <summary>One end of a nested range model, with the range it belongs to and the other end beside it.</summary>
    private sealed record RangeEnd(
        string Property, string ModelType, string ModelName, string ValueObject, ValueObjectMember Start, ValueObjectMember End,
        bool Ruled);

    // The two ends of a nested range model, in the order its primary constructor takes them; none for any other model.
    private static IEnumerable<RangeEnd> RangeEnds(ValueObjectShape nested)
    {
        if (nested is not { ValueBase: "Range", SingleValue: false, ConstructorOrder.Count: 2, Members.Count: 2 } ||
            nested.Members.Any(static m => m.ValueObject is not null))
        {
            yield break;
        }

        var start = nested.Members.First(m => string.Equals(m.Name, nested.ConstructorOrder[0], StringComparison.OrdinalIgnoreCase));
        var end = nested.Members.First(m => string.Equals(m.Name, nested.ConstructorOrder[1], StringComparison.OrdinalIgnoreCase));
        foreach (var member in new[] { start, end })
        {
            yield return new RangeEnd(member.Name, member.ModelType, nested.ModelName, nested.TypeName, start, end, member.RequiredByType);
        }
    }

    // The delegate a field keeps for one end: handed the nested model it is on, so it reads the other end.
    private static void AppendBesideRule(StringBuilder s, Entity entity, string modelKey, RangeEnd end)
    {
        s.Append("    private static readonly global::System.Func<object, ").Append(end.ModelType).Append(", ").Append(Messages).Append("> ")
            .Append(modelKey).Append(end.Property).AppendLine(" =");
        s.Append("        ").Append(ExtensionsType(entity)).Append('.').Append(BesideMethod(end)).AppendLine(";");
    }

    private static string BesideMethod(RangeEnd end) => "__Beside_" + end.ModelName + "_" + end.Property;

    private static string InRangeMethod(string modelName) => "__InRange_" + modelName;

    // Beside the rule methods: per end, its own rule first (required) and then the range's; per range, the ask.
    private static void AppendRangeRuleMethods(StringBuilder s, Entity entity)
    {
        foreach (var nested in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            var ends = RangeEnds(nested).ToList();
            if (ends.Count == 0)
            {
                continue;
            }

            var holder = FormModelType(entity) + "." + nested.ModelName;
            foreach (var end in ends)
            {
                AppendBesideMethod(s, holder, end);
            }

            AppendInRangeMethod(s, nested, ends[0].Start, ends[0].End);
        }
    }

    private static void AppendBesideMethod(StringBuilder s, string holder, RangeEnd end)
    {
        var isStart = string.Equals(end.Property, end.Start.Name, StringComparison.Ordinal);
        var asked = isStart
            ? "(value, model." + end.End.Name + ", \"" + end.Property + "\")"
            : "(model." + end.Start.Name + ", value, \"" + end.Property + "\")";

        s.Append("    internal static ").Append(Messages).Append(' ').Append(BesideMethod(end)).Append("(object holder, ")
            .Append(end.ModelType).AppendLine(" value)");
        s.AppendLine("    {");
        if (end.Ruled)
        {
            s.Append("        var own = ").Append(RuleMethod(end.ModelName + "_", end.Property)).AppendLine("(value);");
            s.AppendLine("        if (global::System.Linq.Enumerable.Any(own))");
            s.AppendLine("        {");
            s.AppendLine("            return own;");
            s.AppendLine("        }");
            s.AppendLine();
        }

        s.Append("        return holder is ").Append(holder).Append(" model ? ").Append(InRangeMethod(end.ModelName)).Append(asked)
            .Append(" : ").Append(NoMessages).AppendLine(";");
        s.AppendLine("    }");
        s.AppendLine();
    }

    // What the range's own TryCreate says against the two ends, under `member`; nothing while an end it needs is empty.
    private static void AppendInRangeMethod(StringBuilder s, ValueObjectShape nested, ValueObjectMember start, ValueObjectMember end)
    {
        s.Append("    internal static ").Append(Messages).Append(' ').Append(InRangeMethod(nested.ModelName)).Append('(')
            .Append(start.ModelType).Append(" start, ").Append(end.ModelType).AppendLine(" end, string member)");
        s.AppendLine("    {");

        var needed = new List<string>();
        if (!start.Nullable)
        {
            needed.Add("start is not { } __start");
        }

        if (!end.Nullable)
        {
            needed.Add("end is not { } __end");
        }

        if (needed.Count > 0)
        {
            s.Append("        if (").Append(string.Join(" || ", needed)).AppendLine(")");
            s.AppendLine("        {");
            s.Append("            return ").Append(NoMessages).AppendLine(";");
            s.AppendLine("        }");
            s.AppendLine();
        }

        s.Append("        ").Append(nested.TypeName).Append(".TryCreate(").Append(start.Nullable ? "start" : "__start").Append(", ")
            .Append(end.Nullable ? "end" : "__end").AppendLine(", out _, out var __problems);");
        s.AppendLine("        return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(");
        s.AppendLine("            global::System.Linq.Enumerable.Where(__problems, p => string.Equals(p.Field, member, global::System.StringComparison.Ordinal)),");
        s.AppendLine("            static p => p.Message));");
        s.AppendLine("    }");
        s.AppendLine();
    }
}
