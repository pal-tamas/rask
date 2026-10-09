using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// The rules a form over a generated model asks BY ITSELF: a value object's own rule for the field that holds
// it, and the source row's key the store's rules need to leave that row out.
public sealed partial class ModelInputGenerator
{
    private const string RaskValidation = "global::Rask.Core.Forms.RaskValidation";
    private const string ValidateDelegate = "global::Rask.Core.Forms.Validate";
    private const string StringSequence = "global::System.Collections.Generic.IEnumerable<string>";
    private const string FieldFailure = "global::Rask.Wire.FieldFailure";
    private const string Failures = "global::System.Collections.Generic.IReadOnlyList<" + FieldFailure + ">";
    private const string NoMessages = "global::System.Array.Empty<string>()";

    /// <summary>
    ///     What the compilation offers a form's rules to be announced through. A library that models its data
    ///     and draws no form references neither, and its models are then generated without them.
    /// </summary>
    private readonly record struct Seams(bool FieldRules, bool StoreRules);

    private static IncrementalValueProvider<Seams> FormSeams(IncrementalGeneratorInitializationContext context) =>
        context.CompilationProvider.Select(static (compilation, _) =>
        {
            var validation = compilation.GetTypeByMetadataName("Rask.Core.Forms.RaskValidation");

            return new Seams(
                validation?.GetMembers("RegisterFieldRules").Length > 0,
                validation?.GetMembers("RegisterStoreRules").Length > 0 &&
                compilation.GetTypeByMetadataName("Rask.Core.Forms.IStoreRules") is not null &&
                compilation.GetTypeByMetadataName("Rask.Data.GeneratedStoreRules") is not null);
        });

    /// <summary>How a one-value value object states its rule, when it does.</summary>
    private enum ValueRule
    {
        None,
        Sync,
        Async,
    }

    /// <summary>
    ///     The rule a one-value value object carries by CONVENTION: a public static <c>Validate</c> taking the
    ///     value it holds and returning the messages — as they are, or awaited.
    /// </summary>
    /// <remarks>
    ///     No interface and no attribute: the value object stays a plain type. Anything that is not exactly this
    ///     shape is not a rule.
    /// </remarks>
    private static ValueRule RuleOf(ModelValueObject valueObject)
    {
        if (!valueObject.SingleValue)
        {
            return ValueRule.None;
        }

        var held = valueObject.Members[0].Property.Type;

        foreach (var method in valueObject.Type.GetMembers("Validate").OfType<IMethodSymbol>())
        {
            if (method is not { IsStatic: true, DeclaredAccessibility: Accessibility.Public, Parameters.Length: 1, IsGenericMethod: false } ||
                !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, held))
            {
                continue;
            }

            if (IsMessages(method.ReturnType))
            {
                return ValueRule.Sync;
            }

            if (method.ReturnType is INamedTypeSymbol { Name: "ValueTask" or "Task", TypeArguments.Length: 1 } awaited &&
                string.Equals(awaited.ContainingNamespace.ToDisplayString(), "System.Threading.Tasks", System.StringComparison.Ordinal) &&
                IsMessages(awaited.TypeArguments[0]))
            {
                return ValueRule.Async;
            }
        }

        return ValueRule.None;
    }

    private static bool IsMessages(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "IEnumerable", TypeArguments.Length: 1 } sequence &&
        sequence.TypeArguments[0].SpecialType == SpecialType.System_String &&
        string.Equals(sequence.ContainingNamespace.ToDisplayString(), "System.Collections.Generic", System.StringComparison.Ordinal);

    /// <summary>Whether the model remembers which row it was filled from: an aggregate root's, with a key.</summary>
    private static bool HasSelfKey(Entity entity) => !entity.IsChild && entity.IdTypeName is not null;

    // Which stored row the model was filled from. Not a form field: nothing binds it and nothing posts it — the
    // model lives on the server, and ToModel() is the only thing that sets it. The store's rules read it so an
    // edit does not collide with the row being edited.
    private static void AppendSelfKey(StringBuilder s, Entity entity)
    {
        if (!HasSelfKey(entity))
        {
            return;
        }

        s.Append("    /// <summary>The key of the <see cref=\"").Append(entity.FullyQualifiedName)
            .AppendLine("\" /> this model was filled from by <c>ToModel()</c>, or <c>null</c> for a new one.</summary>");
        s.AppendLine("    /// <remarks>");
        s.AppendLine("    /// Read by the store's rules, so a check before the save leaves the row being edited out of it. It is");
        s.AppendLine("    /// not a field: no form binds it, and a save takes its id beside the model, never from here.");
        s.AppendLine("    /// </remarks>");
        s.Append("    internal ").Append(entity.IdTypeName).AppendLine("? __Key { get; set; }");
        s.AppendLine();
    }

    // One kept delegate per property that holds a rule, and a switch that hands it back: a bound field asks on
    // every render, so nothing here is built when it does.
    private static void AppendFieldRules(StringBuilder s, Entity entity)
    {
        var models = entity.AnnouncesFieldRules ? RuledModels(entity).ToList() : [];
        var store = AsksTheStore(entity);
        if (models.Count == 0 && !store)
        {
            return;
        }

        s.AppendLine();
        s.Append("/// <summary>The rules a form over ").Append(entity.Name).Append(ModelSuffix)
            .AppendLine(" asks by itself, announced to the forms that bind it.</summary>");
        s.Append("file static class __").Append(entity.Name).Append(ModelSuffix).AppendLine("Rules");
        s.AppendLine("{");
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    internal static void Register()");
        s.AppendLine("    {");
        foreach (var model in models)
        {
            s.Append("        ").Append(RaskValidation).Append(".RegisterFieldRules(typeof(").Append(model.Type)
                .Append("), ").Append(model.Key).AppendLine("RuleOf);");
        }

        if (store)
        {
            s.Append("        ").Append(RaskValidation).Append(".RegisterStoreRules(typeof(").Append(FormModelType(entity))
                .AppendLine("), StoreRules);");
        }

        s.AppendLine("    }");

        foreach (var model in models)
        {
            s.AppendLine();
            foreach (var rule in model.Rules)
            {
                AppendRule(s, model.Key, rule);
            }

            s.AppendLine();
            s.Append("    private static global::System.Delegate? ").Append(model.Key).AppendLine("RuleOf(string property) =>");
            s.AppendLine("        property switch");
            s.AppendLine("        {");
            foreach (var property in model.Rules.Select(static rule => rule.Property))
            {
                s.Append("            \"").Append(property).Append("\" => ").Append(model.Key).Append(property).AppendLine(",");
            }

            s.AppendLine("            _ => null,");
            s.AppendLine("        };");
        }

        if (store)
        {
            AppendStoreRules(s, entity);
        }

        s.AppendLine("}");
    }

    /// <summary>
    ///     Whether a form over the root's model asks the store before it saves: the aggregate's own
    ///     <c>Configure</c> declares a unique rule that carries a message — <c>IsUnique("…")</c>.
    /// </summary>
    /// <remarks>
    ///     Decided here, from the source, because a form that asks the store waits for it: a model with no such
    ///     rule must not gain a validator to await. A rule declared anywhere else is still enforced by the save.
    /// </remarks>
    private static bool AsksTheStore(Entity entity) => !entity.IsChild && entity.DeclaresUniqueRules;

    private static bool ConfiguresUniqueRule(INamedTypeSymbol symbol, CancellationToken cancellationToken) =>
        symbol.GetMembers("Configure").OfType<IMethodSymbol>()
            .Where(static method => method.IsStatic)
            .SelectMany(static method => method.DeclaringSyntaxReferences)
            .SelectMany(reference => reference.GetSyntax(cancellationToken).DescendantNodes())
            .OfType<InvocationExpressionSyntax>()
            .Any(static call =>
                call is { ArgumentList.Arguments.Count: > 0, Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsUnique" } });

    // The model's values by the ENTITY's property names, read through the typed properties, and the key of the
    // row it was filled from. Both are static, so announcing them builds nothing per form.
    private static void AppendStoreRules(StringBuilder s, Entity entity)
    {
        var model = FormModelType(entity);

        s.AppendLine();
        s.AppendLine("    private static global::Rask.Core.Forms.IStoreRules? StoreRules(global::System.IServiceProvider? services) =>");
        s.AppendLine("        global::Rask.Data.GeneratedStoreRules.CanAsk(services) ? new Store(services) : null;");
        s.AppendLine();
        s.AppendLine("    private sealed class Store(global::System.IServiceProvider? services) : global::Rask.Core.Forms.IStoreRules");
        s.AppendLine("    {");
        s.Append("        public global::System.Threading.Tasks.ValueTask<").Append(Failures).AppendLine("> Check(");
        s.AppendLine("            object model, string? field, global::System.Threading.CancellationToken cancellationToken) =>");
        s.Append("            model is ").Append(model).AppendLine(" typed");
        s.Append("                ? global::Rask.Data.GeneratedStoreRules.Check<").Append(entity.FullyQualifiedName).Append(", ").Append(model)
            .AppendLine(">(services, typed, field, ValueOf, KeyOf, cancellationToken)");
        s.Append("                : new(global::System.Array.Empty<").Append(FieldFailure).AppendLine(">());");
        s.AppendLine("    }");
        s.AppendLine();
        s.Append("    private static object? ValueOf(").Append(model).AppendLine(" model, string property) =>");
        s.AppendLine("        property switch");
        s.AppendLine("        {");
        foreach (var name in entity.Members
                     .Where(static m => m.Role == ModelMemberRole.Value && m.ValueObject is null or { SingleValue: true })
                     .Select(static m => m.Name))
        {
            s.Append("            \"").Append(name).Append("\" => model.").Append(name).AppendLine(",");
        }

        s.AppendLine("            _ => null,");
        s.AppendLine("        };");
        s.AppendLine();
        s.Append("    private static object? KeyOf(").Append(model).Append(" model) => ")
            .Append(HasSelfKey(entity) ? "model.__Key" : "null").AppendLine(";");
    }

    // A form field holds the value, not the value object, and may hold none yet — so the rule is asked only
    // about a value that is there. What an empty field means is `[Required]`'s to say.
    private static void AppendRule(StringBuilder s, string modelKey, FieldRule rule)
    {
        var call = rule.ValueObject + ".Validate(held)";

        if (rule.Kind == ValueRule.Sync)
        {
            s.Append("    private static readonly ").Append(ValidateDelegate).Append('<').Append(rule.ModelType).Append("> ")
                .Append(modelKey).Append(rule.Property).AppendLine(" =");
            s.Append("        static value => value is { } held ? ").Append(call).Append(" : ").Append(NoMessages).AppendLine(";");
            return;
        }

        s.Append("    private static readonly global::System.Func<").Append(rule.ModelType)
            .Append(", global::System.Threading.Tasks.ValueTask<").Append(StringSequence).Append(">> ")
            .Append(modelKey).Append(rule.Property).AppendLine(" =");
        s.Append("        static async value => value is { } held ? await ").Append(call).Append(" : ").Append(NoMessages).AppendLine(";");
    }

    // The root model and each nested value-object model that has at least one rule, with a prefix that keeps
    // their members apart inside the one rules class.
    private static IEnumerable<RuledModel> RuledModels(Entity entity)
    {
        var root = entity.Members
            .Where(static m => m.ValueObject is { SingleValue: true, Rule: not ValueRule.None })
            .Select(static m => new FieldRule(m.Name, m.ModelType, m.ValueObject!.TypeName, m.ValueObject.Rule))
            .ToList();

        if (root.Count > 0)
        {
            yield return new RuledModel(FormModelType(entity), "Root_", root);
        }

        foreach (var nested in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            var rules = nested.Members
                .Where(static m => m.ValueObject is { SingleValue: true, Rule: not ValueRule.None })
                .Select(static m => new FieldRule(m.Name, m.ModelType, m.ValueObject!.TypeName, m.ValueObject.Rule))
                .ToList();

            if (rules.Count > 0)
            {
                yield return new RuledModel(FormModelType(entity) + "." + nested.ModelName, nested.ModelName + "_", rules);
            }
        }
    }

    private sealed record RuledModel(string Type, string Key, List<FieldRule> Rules);

    private sealed record FieldRule(string Property, string ModelType, string ValueObject, ValueRule Kind);
}
