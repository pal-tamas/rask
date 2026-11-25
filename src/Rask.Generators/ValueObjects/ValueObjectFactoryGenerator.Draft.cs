using System.Linq;
using System.Text;

namespace Rask.Generators;

public sealed partial class ValueObjectFactoryGenerator
{
    // `Opening.__Draft`: what a form over a model of its own holds of a value object of several values while its members
    // are typed in one at a time — each member as typed, empty until it is (a value type held as its nullable twin), the
    // value object made from them through TryCreate, and a member that may not be empty and is said under its name.
    private static void AppendDraft(StringBuilder s, ValueObject value)
    {
        if (!value.GroupedByForms)
        {
            return;
        }

        s.AppendLine();
        s.AppendLine("    /// <summary>What a form holds of it while its members are typed in one at a time: each as typed, made through <c>TryCreate</c>.</summary>");
        s.AppendLine(Hidden);
        s.AppendLine("    public sealed class __Draft : global::Rask.Values.ValueDraft");
        s.AppendLine("    {");
        foreach (var member in value.Parameters)
        {
            s.Append("        private ").Append(member.OptionalType).Append(" __").Append(member.Name).AppendLine(";");
        }

        foreach (var member in value.Parameters)
        {
            s.AppendLine();
            s.Append("        /// <summary>Its <c>").Append(member.Name).AppendLine("</c>, as typed.</summary>");
            s.Append("        public ").Append(member.OptionalType).Append(' ').Append(member.Name)
                .Append(" { get => __").Append(member.Name).Append("; set { __").Append(member.Name).AppendLine(" = value; OnTyped(); } }");
        }

        s.AppendLine();
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override bool HoldsNothing => ")
            .Append(string.Join(" && ", value.Parameters.Select(static p => Empty(Drafted(p))))).AppendLine(";");

        s.AppendLine();
        s.AppendLine("        /// <inheritdoc />");
        s.AppendLine("        protected override void FillFrom(object? value)");
        s.AppendLine("        {");
        s.Append("            var __value = value as ").Append(value.FullyQualifiedName).AppendLine(";");
        foreach (var name in value.Parameters.Select(static p => p.Name))
        {
            s.Append("            __").Append(name).Append(" = __value?.").Append(name).AppendLine(";");
        }

        s.AppendLine("        }");

        AppendDraftMake(s, value);
        s.AppendLine("    }");
    }

    private static void AppendDraftMake(StringBuilder s, ValueObject value)
    {
        s.AppendLine();
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override object? TryMake(out ").Append(ProblemList).AppendLine(" problems)");
        s.AppendLine("        {");
        var required = value.Parameters.Where(static p => !p.Nullable).ToList();
        if (required.Count > 0)
        {
            s.Append("            global::System.Collections.Generic.List<").Append(ProblemType).AppendLine(">? __missing = null;");
            foreach (var member in required)
            {
                s.Append("            if (").Append(Empty(Drafted(member))).AppendLine(")");
                s.AppendLine("            {");
                s.Append("                (__missing ??= []).Add(").Append(Rules).Append(".Missing(\"").Append(member.Name).AppendLine("\"));");
                s.AppendLine("            }");
                s.AppendLine();
            }

            s.AppendLine("            if (__missing is not null)");
            s.AppendLine("            {");
            s.AppendLine("                problems = __missing;");
            s.AppendLine("                return null;");
            s.AppendLine("            }");
            s.AppendLine();
        }

        var given = string.Join(", ", value.Parameters.Select(static p => Given(Drafted(p))));
        s.Append("            return ").Append(value.FullyQualifiedName).Append(".TryCreate(").Append(given)
            .AppendLine(", out var __made, out problems) ? __made : null;");
        s.AppendLine("        }");
    }

    // The member as the draft holds it: its field.
    private static Parameter Drafted(Parameter member) => member with { Name = "__" + member.Name };
}
