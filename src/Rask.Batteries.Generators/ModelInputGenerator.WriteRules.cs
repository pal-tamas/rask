using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// What a model is asked before it becomes an aggregate, by the generated writes that take one, whether a form
// is behind them or not: each value object's own rule, then the validation attributes. The rules are METHODS
// emitted here once, and the delegates a form keeps point at them, so the two cannot drift.
public sealed partial class ModelInputGenerator
{
    private const string ModelRules = "global::Rask.Data.GeneratedModelRules";
    // What two rows of one key say where the app has no strings catalog: the catalog's English, as a literal.
    private const string DuplicateRowEnglish = "\"The same row is in this list twice.\"";

    private const string FailureList = "global::System.Collections.Generic.IList<" + FieldFailure + ">?";

    /// <summary>Whether a model has anything to be asked before a write.</summary>
    private readonly record struct RuleReach(bool Checked);

    private static string ExtensionsType(Entity entity) => FormModelType(entity) + "Extensions";

    private static string RuleMethod(string modelKey, string property) => "__Rule_" + modelKey + property;

    private static bool Validates(Entity entity) =>
        entity.ValidatingChildren.Count > 0 || OwnReach(entity).Checked;

    private static RuleReach OwnReach(Entity entity)
    {
        return new RuleReach(
            RuledModels(entity).Any(static m => m.Rules.Count > 0) || CheckedMembers(entity).Any() ||
            HeldValueObjects(entity).Any(static m => Asks(m.ValueObject!, m.Nullable)) ||
            entity.Children.Any(static c => c.ByParts) || entity.OwnRules is not null);
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
            all = new RuleReach(all.Checked || theirs.Checked);
        }

        walking.Remove(name);
        return reach[name] = all;
    }

    private static IEnumerable<string> ChildrenThat(
        Entity entity, Dictionary<string, RuleReach> reach, Func<RuleReach, bool> wanted) =>
        entity.Children
            .Where(c => reach.TryGetValue(c.ChildTypeName, out var theirs) && wanted(theirs))
            .Select(static c => c.Name);

    private static void AppendValidateCall(StringBuilder s) => s.AppendLine("            __Validate(model);");

    private static void AppendReturn(StringBuilder s, string write) =>
        s.Append("            return ").Append(write).AppendLine(";");

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

        AppendCollect(s, entity);

        AppendRuleMethods(s, entity);
        AppendOwnMethods(s, entity);

        if (CheckedMembers(entity).Any() || HeldValueObjects(entity).Any(static m => HasNestedChecks(m.ValueObject!)))
        {
            AppendCheck(s, entity.ChecksFromCatalog);
        }
    }

    // Every failure of this model and what it holds, each under the path a form names the field by. `path` is
    // what leads here from the root — "" for the root itself, "Lines[2]." for a row.
    private static void AppendCollect(StringBuilder s, Entity entity)
    {
        s.Append("    internal static ").Append(FailureList).Append(" __Collect(")
            .Append(FormModelType(entity)).Append(" model, string path, ").Append(FailureList).AppendLine(" failures)");
        s.AppendLine("    {");

        var held = 0;
        foreach (var member in entity.Members.Where(static m => m.Role == ModelMemberRole.Value))
        {
            if (member.RequiredByType)
            {
                s.Append("        failures = ").Append(ModelRules).Append(".Told(failures, path, \"").Append(member.Name)
                    .Append("\", ").Append(RuleMethod("Root_", member.Name)).Append("(model.").Append(member.Name).AppendLine("));");
            }
            else if (member.ValueObject is { } valueObject)
            {
                AppendAsk(s, "        ", "model." + member.Name, member.Name, "Root_", valueObject, member.Nullable, ref held);
            }
        }

        foreach (var child in entity.Children.Where(c => entity.ValidatingChildren.Contains(c.Name, StringComparer.Ordinal)))
        {
            AppendAskRows(s, child);
        }

        foreach (var child in entity.Children.Where(static c => c.ByParts))
        {
            AppendTwiceGiven(s, child, entity.DuplicateRowFromCatalog);
        }

        foreach (var member in CheckedMembers(entity))
        {
            foreach (var check in member.Checks)
            {
                s.Append("        failures = __Check(").Append(check).Append(", model.").Append(member.Name).Append(", \"")
                    .Append(member.Name).Append("\", ").Append(member.Named ?? Quoted(member.Name)).AppendLine(", model, path, failures);");
            }
        }

        AppendOwnCall(s, entity);
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
        ref int local)
    {
        if (valueObject.SingleValue)
        {
            if (valueObject.HasRule)
            {
                AppendAskRule(s, indent, access, field, modelKey);
            }

            return;
        }

        if (!Asks(valueObject, nullable))
        {
            return;
        }

        var missing = indent + "    failures = " + ModelRules + ".Told(failures, path, \"" + field + "\", " + ModelRules + ".Required(__RequiredText()));";

        if (!valueObject.Members.Any(AsksMember))
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
            AppendAskMember(s, indent + "    ", held, field, valueObject, member, ref local);
        }

        s.Append(indent).AppendLine("}");

        if (!nullable)
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
        StringBuilder s, string indent, string held, string field, ValueObjectShape owner, ValueObjectMember member, ref int local)
    {
        var access = held + "." + member.Name;
        var path = field + "." + member.Name;
        var modelKey = owner.ModelName + "_";

        if (member.RequiredByType)
        {
            AppendAskRule(s, indent, access, path, modelKey);
        }
        else if (member.ValueObject is { } inner)
        {
            AppendAsk(s, indent, access, path, modelKey, inner, member.Nullable, ref local);
        }

        foreach (var check in member.Checks)
        {
            s.Append(indent).Append("failures = __Check(").Append(check).Append(", ").Append(access).Append(", \"")
                .Append(member.Name).Append("\", ").Append(member.Named ?? Quoted(member.Name)).Append(", ").Append(held).Append(", path + \"").Append(field).AppendLine(".\", failures);");
        }
    }

    private static void AppendAskRule(StringBuilder s, string indent, string access, string field, string modelKey) =>
        s.Append(indent).Append("failures = ").Append(ModelRules).Append(".Told(failures, path, \"").Append(field).Append("\", ")
            .Append(RuleMethod(modelKey, field.Substring(field.LastIndexOf('.') + 1))).Append('(').Append(access).AppendLine("));");

    // Whether asking a value object says anything. One of several values that may not be absent is asked for even
    // when nothing inside it has a rule.
    private static bool Asks(ValueObjectShape valueObject, bool nullable) =>
        valueObject.SingleValue
            ? valueObject.HasRule
            : !nullable || valueObject.Members.Any(AsksMember);

    private static bool AsksMember(ValueObjectMember member) =>
        member.RequiredByType || member.Checks.Count > 0 ||
        (member.ValueObject is { } inner && Asks(inner, member.Nullable));

    // Whether the one sentence is said for a value object itself, here or anywhere inside it.
    private static bool RequiresHeld(ValueObjectShape valueObject, bool nullable) =>
        !valueObject.SingleValue &&
        (!nullable || valueObject.Members.Any(static m => m.ValueObject is { } inner && RequiresHeld(inner, m.Nullable)));

    private static bool HasNestedChecks(ValueObjectShape valueObject) =>
        !valueObject.SingleValue &&
        valueObject.Members.Any(static m => m.Checks.Count > 0 || (m.ValueObject is { } inner && HasNestedChecks(inner)));

    private static void AppendAskRows(StringBuilder s, ChildShape child)
    {
        var collect = child.ChildModelName + "Extensions.__Collect";
        var asked = collect + "(row, path + \"" + child.Name
                    + "[\" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"].\", failures)";

        // Named after the collection: a pattern variable in an if's condition is scoped to the whole method, so a
        // root holding two kinds of rows would otherwise declare `rows` twice.
        var rows = "__" + child.Name + "Rows";

        s.Append("        if (model.").Append(child.Name).Append(" is { } ").Append(rows).AppendLine(")");
        s.AppendLine("        {");
        s.Append("            for (var i = 0; i < ").Append(rows).AppendLine(".Count; i++)");
        s.AppendLine("            {");
        s.Append("                if (").Append(rows).AppendLine("[i] is { } row)");
        s.AppendLine("                {");
        s.Append("                    failures = ").Append(asked).AppendLine(";");
        s.AppendLine("                }");
        s.AppendLine("            }");
        s.AppendLine("        }");
    }

    // A row keyed by several columns is matched by its key, so two posted rows of one key would be one row twice: the
    // second is refused under the list, before anything is loaded. A row whose key is not filled in yet is the
    // required rule's to report, not this one's.
    private static void AppendTwiceGiven(StringBuilder s, ChildShape child, bool fromCatalog)
    {
        var rows = "__" + child.Name + "Keys";
        var key = child.KeyParts.Count == 1
            ? "row." + child.KeyParts[0].Name
            : "(" + string.Join(", ", child.KeyParts.Select(static p => "row." + p.Name)) + ")";
        var keyType = child.KeyParts.Count == 1
            ? child.KeyParts[0].TypeName + "?"
            : "(" + string.Join(", ", child.KeyParts.Select(static p => p.TypeName + "?")) + ")";
        var filled = string.Join(" && ", child.KeyParts.Select(static p => "row." + p.Name + " is not null"));

        s.Append("        if (model.").Append(child.Name).Append(" is { } ").Append(rows).AppendLine(")");
        s.AppendLine("        {");
        s.Append("            var seen = new global::System.Collections.Generic.HashSet<").Append(keyType).AppendLine(">();");
        s.Append("            for (var i = 0; i < ").Append(rows).AppendLine(".Count; i++)");
        s.AppendLine("            {");
        s.Append("                if (").Append(rows).Append("[i] is { } row && ").Append(filled).Append(" && !seen.Add(").Append(key).AppendLine("))");
        s.AppendLine("                {");
        s.Append("                    failures = ").Append(ModelRules).Append(".Told(failures, path, \"").Append(child.Name)
            .Append("\", ").Append(fromCatalog
                ? "global::Rask.Core.Globalization.RaskStrings.Get(global::Rask.Core.Globalization.RaskString.ValidationDuplicateRow, "
                  + DuplicateRowEnglish + ")"
                : DuplicateRowEnglish).AppendLine(");");
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
            s.Append("    internal static ").Append(StringSequence).Append(' ')
                .Append(RuleMethod(key, rule.Property)).Append('(').Append(rule.ModelType).AppendLine(" value) =>");
            s.Append("        ").Append(RuleBody(rule)).AppendLine(";");
            s.AppendLine();
        }

        AppendBesideRuleMethods(s, entity);

        if (rules.Exists(static r => r.Rule.Required) ||
            HeldValueObjects(entity).Any(static m => RequiresHeld(m.ValueObject!, m.Nullable)))
        {
            AppendRequiredText(s, entity);
        }
    }

    private static string RuleBody(FieldRule rule)
    {
        var required = ModelRules + ".Required(__RequiredText())";

        // No value object to ask: the property only has to be filled, and blank text is not.
        if (rule.ValueObject is not { } valueObject)
        {
            return (IsText(rule) ? "string.IsNullOrWhiteSpace(value)" : "value is null") + " ? " + required + " : " + NoMessages;
        }

        string Ask(string value) => valueObject + ".__Problems(" + value + ")";

        // A base record calls blank text no value: a field that may be empty is not asked about it.
        if (IsText(rule) && !rule.Required)
        {
            return "string.IsNullOrWhiteSpace(value) ? " + NoMessages + " : " + Ask("value");
        }

        var empty = NoMessages;
        if (rule.Required)
        {
            empty = IsText(rule) ? ModelRules + ".OrRequired(" + Ask("\"\"") + ", __RequiredText())" : required;
        }

        return "value is { } held ? " + Ask("held") + " : " + empty;
    }

    private static string Quoted(string name) => SymbolDisplay.FormatLiteral(name, quote: true);

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
    // A built-in attribute's English is worded in the reader's language where the compilation has Core's catalog.
    private static void AppendCheck(StringBuilder s, bool fromCatalog)
    {
        s.Append("    private static ").Append(FailureList)
            .Append(" __Check(global::System.ComponentModel.DataAnnotations.ValidationAttribute check, ")
            .Append("object? value, string name, string displayName, object model, string path, ").Append(FailureList)
            .AppendLine(" failures)");
        s.AppendLine("    {");
        s.AppendLine("        // The trim-safe context: it names the field by its [Display] name without reflecting over the model, and");
        s.AppendLine("        // hands a rule the services of the work in progress, as the form does.");
        s.Append("        var context = new global::System.ComponentModel.DataAnnotations.ValidationContext(model, displayName, ").Append(AmbientServices)
            .AppendLine(", null) { MemberName = name };");
        s.AppendLine("        return check.GetValidationResult(value, context) is { ErrorMessage: { } error }");
        s.Append("            ? ").Append(ModelRules).Append(".Told(failures, path, name, ")
            .Append(fromCatalog ? "global::Rask.Core.Forms.DataAnnotationsFieldValidator.Message(check, value, error)" : "error").AppendLine(")");
        s.AppendLine("            : failures;");
        s.AppendLine("    }");
        s.AppendLine();
    }
}
