using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// What a model is asked before it becomes an aggregate, by the generated writes that take one, whether a form
// is behind them or not: each value object's own rule, then the validation attributes. The rules are METHODS
// emitted here once, and the delegates a form keeps point at them, so the two cannot drift.
public sealed partial class ModelInputGenerator
{
    private const string ModelRules = "global::Rask.Data.GeneratedModelRules";
    private const string FailureList = "global::System.Collections.Generic.IList<" + FieldFailure + ">?";

    /// <summary>Whether a model has anything to be asked before a write, and whether any of it is awaited.</summary>
    private readonly record struct RuleReach(bool Checked, bool Awaited);

    private static string ExtensionsType(Entity entity) => FormModelType(entity) + "Extensions";

    private static string RuleMethod(string modelKey, string property) => "__Rule_" + modelKey + property;

    private static bool Awaits(Entity entity) =>
        entity.AwaitingChildren.Count > 0 || RuledModels(entity).Any(static m => m.Rules.Any(static r => r.Kind == ValueRule.Async));

    private static bool Validates(Entity entity) =>
        entity.ValidatingChildren.Count > 0 || OwnReach(entity).Checked;

    private static RuleReach OwnReach(Entity entity)
    {
        var rules = RuledModels(entity).SelectMany(static m => m.Rules).ToList();

        return new RuleReach(
            rules.Count > 0 || CheckedMembers(entity).Any() || HeldValueObjects(entity).Any(static m => Asks(m.ValueObject!, m.Nullable, ValueRule.Sync)),
            rules.Exists(static r => r.Kind == ValueRule.Async));
    }

    // The root's value objects of several values: each is a nested model that is there or is not.
    private static IEnumerable<Member> HeldValueObjects(Entity entity) =>
        entity.Members.Where(static m => m.Role == ModelMemberRole.Value && m.ValueObject is { SingleValue: false });

    private static IEnumerable<Member> CheckedMembers(Entity entity) =>
        entity.Members.Where(static m => m.Role == ModelMemberRole.Value && m.Write is not null && m.Checks.Count > 0);

    // A root asks its rows, and a row may hold rows: what each model reaches is its own plus its children's.
    private static Dictionary<string, RuleReach> RuleReachOf(List<Entity> entities)
    {
        var byName = entities
            .GroupBy(static e => e.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);
        var reach = new Dictionary<string, RuleReach>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            Reach(entity.FullyQualifiedName, byName, reach, new HashSet<string>(StringComparer.Ordinal));
        }

        return reach;
    }

    private static RuleReach Reach(
        string name, Dictionary<string, Entity> byName, Dictionary<string, RuleReach> reach, HashSet<string> walking)
    {
        if (reach.TryGetValue(name, out var known))
        {
            return known;
        }

        if (!byName.TryGetValue(name, out var entity) || entity.Refusal != Refusal.None || !walking.Add(name))
        {
            return default;
        }

        var all = OwnReach(entity);
        foreach (var child in entity.Children)
        {
            var theirs = Reach(child.ChildTypeName, byName, reach, walking);
            all = new RuleReach(all.Checked || theirs.Checked, all.Awaited || theirs.Awaited);
        }

        walking.Remove(name);
        return reach[name] = all;
    }

    private static IEnumerable<string> ChildrenThat(
        Entity entity, Dictionary<string, RuleReach> reach, Func<RuleReach, bool> wanted) =>
        entity.Children
            .Where(c => reach.TryGetValue(c.ChildTypeName, out var theirs) && wanted(theirs))
            .Select(static c => c.Name);

    private static void AppendValidateCall(StringBuilder s, Entity entity) =>
        s.AppendLine(Awaits(entity)
            ? "            await __ValidateAwaited(model).ConfigureAwait(false);"
            : "            __Validate(model);");

    private static void AppendReturn(StringBuilder s, Entity entity, string write) =>
        s.Append("            return ").Append(Awaits(entity) ? "await " + write + ".ConfigureAwait(false)" : write).AppendLine(";");

    // The server's own check of what a model holds, before anything is written. A model with no rule and no
    // attribute gets an empty method: nothing is built and nothing is asked.
    private static void AppendValidate(StringBuilder s, Entity entity)
    {
        var modelType = FormModelType(entity);
        var validates = Validates(entity);

        s.Append("    internal static void __Validate(").Append(modelType).AppendLine(" model)");
        s.AppendLine("    {");
        if (validates)
        {
            s.AppendLine("        if (__Collect(model, \"\", null) is { } failures)");
            s.AppendLine("        {");
            s.Append("            throw ").Append(ModelRules).AppendLine(".Refused(failures);");
            s.AppendLine("        }");
        }

        s.AppendLine("    }");
        s.AppendLine();

        if (Awaits(entity))
        {
            s.Append("    internal static async global::System.Threading.Tasks.Task __ValidateAwaited(").Append(modelType).AppendLine(" model)");
            s.AppendLine("    {");
            s.AppendLine("        var failures = await __CollectAwaited(model, \"\", __Collect(model, \"\", null)).ConfigureAwait(false);");
            s.AppendLine("        if (failures is not null)");
            s.AppendLine("        {");
            s.Append("            throw ").Append(ModelRules).AppendLine(".Refused(failures);");
            s.AppendLine("        }");
            s.AppendLine("    }");
            s.AppendLine();
        }

        AppendCollect(s, entity, awaited: false);
        if (Awaits(entity))
        {
            AppendCollect(s, entity, awaited: true);
        }

        AppendRuleMethods(s, entity);

        if (CheckedMembers(entity).Any() || HeldValueObjects(entity).Any(static m => HasNestedChecks(m.ValueObject!)))
        {
            AppendCheck(s);
        }
    }

    // Every failure of this model and what it holds, each under the path a form names the field by. `path` is
    // what leads here from the root — "" for the root itself, "Lines[2]." for a row.
    private static void AppendCollect(StringBuilder s, Entity entity, bool awaited)
    {
        var kind = awaited ? ValueRule.Async : ValueRule.Sync;

        s.Append("    internal static ")
            .Append(awaited ? "async global::System.Threading.Tasks.Task<" + FailureList + "> __CollectAwaited(" : FailureList + " __Collect(")
            .Append(FormModelType(entity)).Append(" model, string path, ").Append(FailureList).AppendLine(" failures)");
        s.AppendLine("    {");

        var held = 0;
        foreach (var member in entity.Members.Where(static m => m.Role == ModelMemberRole.Value))
        {
            if (member.RequiredByType)
            {
                if (!awaited)
                {
                    s.Append("        failures = ").Append(ModelRules).Append(".Told(failures, path, \"").Append(member.Name)
                        .Append("\", ").Append(RuleMethod("Root_", member.Name)).Append("(model.").Append(member.Name).AppendLine("));");
                }
            }
            else if (member.ValueObject is { } valueObject)
            {
                AppendAsk(s, "        ", "model." + member.Name, member.Name, "Root_", valueObject, member.Nullable, kind, ref held);
            }
        }

        foreach (var child in entity.Children.Where(c => (awaited ? entity.AwaitingChildren : entity.ValidatingChildren).Contains(c.Name, StringComparer.Ordinal)))
        {
            AppendAskRows(s, child, awaited);
        }

        if (!awaited)
        {
            foreach (var member in CheckedMembers(entity))
            {
                foreach (var check in member.Checks)
                {
                    s.Append("        failures = __Check(").Append(check).Append(", model.").Append(member.Name).Append(", \"")
                        .Append(member.Name).AppendLine("\", model, path, failures);");
                }
            }
        }

        s.AppendLine("        return failures;");
        s.AppendLine("    }");
        s.AppendLine();
    }

    // One value object: its own rule when it holds one value; when it holds several, that it is there at all unless
    // it may be absent, and then each member's rule under the member's own path. `local` numbers the pattern
    // variables: one declared in an `if` is in scope for the rest of the block, so two value objects side by side
    // must not share a name.
    private static void AppendAsk(
        StringBuilder s, string indent, string access, string field, string modelKey, ValueObjectShape valueObject, bool nullable,
        ValueRule kind, ref int local)
    {
        if (valueObject.SingleValue)
        {
            if (valueObject.Rule == kind)
            {
                AppendAskRule(s, indent, access, field, modelKey, kind);
            }

            return;
        }

        if (!Asks(valueObject, nullable, kind))
        {
            return;
        }

        var missing = indent + "    failures = " + ModelRules + ".Told(failures, path, \"" + field + "\", " + ModelRules + ".Required(__RequiredText()));";

        if (!valueObject.Members.Any(m => AsksMember(m, kind)))
        {
            s.Append(indent).Append("if (").Append(access).AppendLine(" is null)");
            s.Append(indent).AppendLine("{");
            s.AppendLine(missing);
            s.Append(indent).AppendLine("}");
            return;
        }

        var held = "__held" + (local++).ToString(CultureInfo.InvariantCulture);
        s.Append(indent).Append("if (").Append(access).Append(" is { } ").Append(held)
            .Append(AndNotEmpty(held, valueObject, nullable)).AppendLine(")");
        s.Append(indent).AppendLine("{");
        foreach (var member in valueObject.Members)
        {
            AppendAskMember(s, indent + "    ", held, field, valueObject, member, kind, ref local);
        }

        s.Append(indent).AppendLine("}");

        if (kind == ValueRule.Sync && !nullable)
        {
            s.Append(indent).AppendLine("else");
            s.Append(indent).AppendLine("{");
            s.AppendLine(missing);
            s.Append(indent).AppendLine("}");
        }
    }

    // One member of a nested model, under the path that reaches it: required by its type, or asked what the value
    // object it holds says, and then the validation attributes the value object's property carries.
    private static void AppendAskMember(
        StringBuilder s, string indent, string held, string field, ValueObjectShape owner, ValueObjectMember member, ValueRule kind,
        ref int local)
    {
        var access = held + "." + member.Name;
        var path = field + "." + member.Name;
        var modelKey = owner.ModelName + "_";

        if (member.RequiredByType)
        {
            if (kind == ValueRule.Sync)
            {
                AppendAskRule(s, indent, access, path, modelKey, kind);
            }
        }
        else if (member.ValueObject is { } inner)
        {
            AppendAsk(s, indent, access, path, modelKey, inner, member.Nullable, kind, ref local);
        }

        if (kind == ValueRule.Sync)
        {
            foreach (var check in member.Checks)
            {
                s.Append(indent).Append("failures = __Check(").Append(check).Append(", ").Append(access).Append(", \"")
                    .Append(member.Name).Append("\", ").Append(held).Append(", path + \"").Append(field).AppendLine(".\", failures);");
            }
        }
    }

    private static void AppendAskRule(StringBuilder s, string indent, string access, string field, string modelKey, ValueRule kind)
    {
        var asked = RuleMethod(modelKey, field.Substring(field.LastIndexOf('.') + 1)) + "(" + access + ")";
        s.Append(indent).Append("failures = ").Append(ModelRules).Append(".Told(failures, path, \"").Append(field).Append("\", ")
            .Append(kind == ValueRule.Async ? "await " + asked + ".ConfigureAwait(false)" : asked).AppendLine(");");
    }

    // Whether asking a value object says anything in this pass. One of several values that may not be absent is
    // asked for in the synchronous pass even when nothing inside it has a rule.
    private static bool Asks(ValueObjectShape valueObject, bool nullable, ValueRule kind) =>
        valueObject.SingleValue
            ? valueObject.Rule == kind
            : (kind == ValueRule.Sync && !nullable) || valueObject.Members.Any(m => AsksMember(m, kind));

    private static bool AsksMember(ValueObjectMember member, ValueRule kind) =>
        (kind == ValueRule.Sync && (member.RequiredByType || member.Checks.Count > 0)) ||
        (member.ValueObject is { } inner && Asks(inner, member.Nullable, kind));

    // Whether the one sentence is said for a value object itself, here or anywhere inside it.
    private static bool RequiresHeld(ValueObjectShape valueObject, bool nullable) =>
        !valueObject.SingleValue &&
        (!nullable || valueObject.Members.Any(static m => m.ValueObject is { } inner && RequiresHeld(inner, m.Nullable)));

    private static bool HasNestedChecks(ValueObjectShape valueObject) =>
        !valueObject.SingleValue &&
        valueObject.Members.Any(static m => m.Checks.Count > 0 || (m.ValueObject is { } inner && HasNestedChecks(inner)));

    private static void AppendAskRows(StringBuilder s, ChildShape child, bool awaited)
    {
        var collect = child.ChildModelName + "Extensions." + (awaited ? "__CollectAwaited" : "__Collect");
        var asked = collect + "(row, path + \"" + child.Name
                    + "[\" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"].\", failures)";

        s.Append("        if (model.").Append(child.Name).AppendLine(" is { } rows)");
        s.AppendLine("        {");
        s.AppendLine("            for (var i = 0; i < rows.Count; i++)");
        s.AppendLine("            {");
        s.AppendLine("                if (rows[i] is { } row)");
        s.AppendLine("                {");
        s.Append("                    failures = ").Append(awaited ? "await " + asked + ".ConfigureAwait(false)" : asked).AppendLine(";");
        s.AppendLine("                }");
        s.AppendLine("            }");
        s.AppendLine("        }");
    }

    // The rule of one field, taking what the FORM holds: the value, or nothing yet. A property the aggregate
    // declares non-nullable is required, and the type says so in its own words where it can — a text value is
    // asked about the empty string. A value that is not text has no empty value to hand over, so there the
    // framework's one sentence stands in.
    private static void AppendRuleMethods(StringBuilder s, Entity entity)
    {
        var rules = RuledModels(entity).SelectMany(static m => m.Rules.Select(r => (m.Key, Rule: r))).ToList();

        foreach (var (key, rule) in rules)
        {
            var awaited = rule.Kind == ValueRule.Async;

            s.Append("    internal static ")
                .Append(awaited ? "async global::System.Threading.Tasks.ValueTask<" + StringSequence + "> " : StringSequence + " ")
                .Append(RuleMethod(key, rule.Property)).Append('(').Append(rule.ModelType).AppendLine(" value) =>");
            s.Append("        ").Append(RuleBody(rule, awaited)).AppendLine(";");
            s.AppendLine();
        }

        if (rules.Exists(static r => r.Rule.Required) ||
            HeldValueObjects(entity).Any(static m => RequiresHeld(m.ValueObject!, m.Nullable)))
        {
            AppendRequiredText(s, entity);
        }
    }

    private static string RuleBody(FieldRule rule, bool awaited)
    {
        var required = ModelRules + ".Required(__RequiredText())";

        // No value object to ask: the property only has to be filled, and blank text is not.
        if (rule.ValueObject is not { } valueObject)
        {
            return (IsText(rule) ? "string.IsNullOrWhiteSpace(value)" : "value is null") + " ? " + required + " : " + NoMessages;
        }

        string Ask(string value) => (awaited ? "await " : "") + valueObject + ".Validate(" + value + ")";

        var empty = NoMessages;
        if (rule.Required)
        {
            empty = IsText(rule) ? ModelRules + ".OrRequired(" + Ask("\"\"") + ", __RequiredText())" : required;
        }

        return "value is { } held ? " + Ask("held") + " : " + empty;
    }

    private static bool IsText(FieldRule rule) => string.Equals(rule.ModelType, "string?", StringComparison.Ordinal);

    // The framework's one sentence for a field that must be filled: from the strings catalog, in the reader's
    // language, where the compilation has one; the same English otherwise. Read only on a path that fails.
    private static void AppendRequiredText(StringBuilder s, Entity entity)
    {
        s.AppendLine("    private static string __RequiredText() =>");
        s.Append("        ").Append(entity.RequiredFromCatalog
            ? "global::Rask.Core.Globalization.RaskStrings.Get(global::Rask.Core.Globalization.RaskString.ValidationRequired, "
              + ModelRules + ".RequiredMessage)"
            : ModelRules + ".RequiredMessage").AppendLine(";");
        s.AppendLine();
    }

    // `model` is whatever holds the field: the root model, or the nested model of a value object.
    private static void AppendCheck(StringBuilder s)
    {
        s.Append("    private static ").Append(FailureList)
            .Append(" __Check(global::System.ComponentModel.DataAnnotations.ValidationAttribute check, ")
            .Append("object? value, string name, object model, string path, ").Append(FailureList)
            .AppendLine(" failures)");
        s.AppendLine("    {");
        s.AppendLine("        // The trim-safe context: it names the model without reflecting over it.");
        s.AppendLine("        var context = new global::System.ComponentModel.DataAnnotations.ValidationContext(model, name, null, null) { MemberName = name };");
        s.AppendLine("        return check.GetValidationResult(value, context) is { ErrorMessage: { } error }");
        s.Append("            ? ").Append(ModelRules).AppendLine(".Told(failures, path, name, error)");
        s.AppendLine("            : failures;");
        s.AppendLine("    }");
        s.AppendLine();
    }
}
