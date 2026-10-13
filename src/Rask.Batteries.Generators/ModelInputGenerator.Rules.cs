using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    private readonly record struct Seams(bool FieldRules, bool StoreRules, bool RequiredSentence, bool RequiredFields, bool OptionalFields);

    private static IncrementalValueProvider<Seams> FormSeams(IncrementalGeneratorInitializationContext context) =>
        context.CompilationProvider.Select(static (compilation, _) =>
        {
            var validation = compilation.GetTypeByMetadataName("Rask.Core.Forms.RaskValidation");

            return new Seams(
                validation?.GetMembers("RegisterFieldRules").Length > 0,
                validation?.GetMembers("RegisterStoreRules").Length > 0 &&
                compilation.GetTypeByMetadataName("Rask.Core.Forms.IStoreRules") is not null &&
                compilation.GetTypeByMetadataName("Rask.Data.GeneratedStoreRules") is not null,
                compilation.GetTypeByMetadataName("Rask.Core.Globalization.RaskStrings") is not null &&
                compilation.GetTypeByMetadataName("Rask.Core.Globalization.RaskString")?.GetMembers("ValidationRequired").Length > 0,
                validation?.GetMembers("RegisterRequiredFields").Length > 0,
                validation?.GetMembers("RegisterOptionalFields").Length > 0);
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

        var rule = valueObject.Type.GetMembers("Validate").OfType<IMethodSymbol>()
            .FirstOrDefault(method => WhyNotARule(method, held) is null);

        if (rule is null)
        {
            return ValueRule.None;
        }

        return IsMessages(rule.ReturnType) ? ValueRule.Sync : ValueRule.Async;
    }

    /// <summary>
    ///     The <c>Validate</c> methods of a one-value value object that has NO rule, each with why it is not
    ///     one — what RASK102 says. Empty once any of them qualifies: the others are then ordinary overloads.
    /// </summary>
    /// <remarks>
    ///     A method that implements an interface or overrides a base member is somebody else's contract —
    ///     <c>IValidatableObject.Validate</c> — and is never taken for an attempt at the rule.
    /// </remarks>
    private static IEnumerable<RuleMiss> RuleMissesOf(ModelValueObject valueObject)
    {
        if (!valueObject.SingleValue || RuleOf(valueObject) != ValueRule.None)
        {
            yield break;
        }

        var held = valueObject.Members[0].Property.Type;

        foreach (var method in valueObject.Type.GetMembers("Validate").OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || method.IsOverride || ImplementsAContract(method) ||
                WhyNotARule(method, held) is not { } reason)
            {
                continue;
            }

            yield return new RuleMiss(
                method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                valueObject.Type.Name,
                reason,
                held.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                SymbolLocation.From(method));
        }
    }

    // Null when the method IS a rule; otherwise the first thing about it that is not.
    private static string? WhyNotARule(IMethodSymbol method, ITypeSymbol held)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return "it is not public";
        }

        if (!method.IsStatic)
        {
            return "it is not static";
        }

        if (method.IsGenericMethod)
        {
            return "it is generic";
        }

        if (method.Parameters.Length != 1)
        {
            return $"it takes {method.Parameters.Length} parameters, not the one value";
        }

        return WhyNotTheValue(method.Parameters[0], held) ?? (IsMessages(method.ReturnType) || IsAwaitedMessages(method.ReturnType)
            ? null
            : $"it returns '{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}', not the messages");
    }

    // Null when the parameter takes the held value as the generated call hands it over: by value, or by 'in'.
    private static string? WhyNotTheValue(IParameterSymbol parameter, ITypeSymbol held)
    {
        if (parameter.RefKind is not (RefKind.None or RefKind.In))
        {
            return $"it takes its value by '{RefWord(parameter.RefKind)}', and a rule is handed the value itself";
        }

        if (SymbolEqualityComparer.Default.Equals(parameter.Type, held))
        {
            return null;
        }

        var declared = parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var value = held.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var reason = $"its parameter is '{declared}', not the '{value}' it holds";

        // The one miss that reads as a rule: 'decimal?' so the rule can say "required" — which it is never asked.
        return IsNullableOf(parameter.Type, held)
            ? reason + $"; declare it '{value}', because an unfilled field is never put to the rule and a property "
                     + "that is not nullable is required by its type"
            : reason;
    }

    private static bool IsNullableOf(ITypeSymbol type, ITypeSymbol held) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable &&
        SymbolEqualityComparer.Default.Equals(nullable.TypeArguments[0], held);

    private static string RefWord(RefKind kind) =>
        kind switch
        {
            RefKind.Ref => "ref",
            RefKind.Out => "out",
            _ => "ref readonly",
        };

    private static bool IsAwaitedMessages(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "ValueTask" or "Task", TypeArguments.Length: 1 } awaited &&
        string.Equals(awaited.ContainingNamespace.ToDisplayString(), "System.Threading.Tasks", System.StringComparison.Ordinal) &&
        IsMessages(awaited.TypeArguments[0]);

    private static bool ImplementsAContract(IMethodSymbol method) =>
        method.ExplicitInterfaceImplementations.Length > 0 ||
        method.ContainingType.AllInterfaces
            .SelectMany(static contract => contract.GetMembers("Validate"))
            .Any(member => SymbolEqualityComparer.Default.Equals(
                method.ContainingType.FindImplementationForInterfaceMember(member), method));

    /// <summary>A <c>Validate</c> that looks meant as a value object's rule and is not one.</summary>
    private sealed record RuleMiss(string Signature, string ValueObject, string Reason, string Held, SymbolLocation? Location);

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
        var optional = entity.AnnouncesOptionalFields ? OptionalSlots(entity).ToList() : [];
        if (models.Count == 0 && !store && optional.Count == 0)
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
        AppendRegistrations(s, entity, models, store);
        AppendOptionalRegistrations(s, optional);
        s.AppendLine("    }");
        AppendOptionalSlots(s, optional);

        foreach (var model in models)
        {
            s.AppendLine();
            foreach (var rule in model.Rules)
            {
                AppendRule(s, entity, model.Key, rule);
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

            if (AnnouncesRequired(entity, model))
            {
                AppendIsRequired(s, model);
            }
        }

        if (store)
        {
            AppendStoreRules(s, entity);
        }

        s.AppendLine("}");
    }

    // One registration per generated model type: a form asks by the exact type its field belongs to.
    private static void AppendRegistrations(StringBuilder s, Entity entity, List<RuledModel> models, bool store)
    {
        foreach (var model in models)
        {
            s.Append("        ").Append(RaskValidation).Append(".RegisterFieldRules(typeof(").Append(model.Type)
                .Append("), ").Append(model.Key).AppendLine("RuleOf);");
            if (AnnouncesRequired(entity, model))
            {
                s.Append("        ").Append(RaskValidation).Append(".RegisterRequiredFields(typeof(").Append(model.Type)
                    .Append("), ").Append(model.Key).AppendLine("IsRequired);");
            }
        }

        if (store)
        {
            s.Append("        ").Append(RaskValidation).Append(".RegisterStoreRules(typeof(").Append(FormModelType(entity))
                .AppendLine("), StoreRules);");
        }
    }

    private static bool AnnouncesRequired(Entity entity, RuledModel model) =>
        entity.AnnouncesRequiredFields && model.Rules.Exists(static rule => rule.Required);

    // What a control asks to say aria-required: exactly the fields whose rule refuses them unfilled, so a field
    // is announced as required where a write would enforce it and nowhere else. A switch over constants.
    private static void AppendIsRequired(StringBuilder s, RuledModel model)
    {
        s.AppendLine();
        s.Append("    private static bool ").Append(model.Key).AppendLine("IsRequired(string property) =>");
        s.Append("        property is ")
            .Append(string.Join(" or ", model.Rules.Where(static rule => rule.Required).Select(static rule => "\"" + rule.Property + "\"")))
            .AppendLine(";");
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
                call is { ArgumentList.Arguments.Count: > 0, Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsUnique" } } &&
                !IsFlag(call.ArgumentList.Arguments[0].Expression));

    // EF Core's own IsUnique(false) / IsUnique(true): a flag, not a message.
    private static bool IsFlag(ExpressionSyntax argument) =>
        argument.IsKind(SyntaxKind.TrueLiteralExpression) || argument.IsKind(SyntaxKind.FalseLiteralExpression);

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

    // The delegate a field keeps: the rule itself is a method beside the writes (ModelInputGenerator.WriteRules),
    // so a form and a write ask the very same thing.
    private static void AppendRule(StringBuilder s, Entity entity, string modelKey, FieldRule rule)
    {
        var type = rule.Kind == ValueRule.Sync
            ? ValidateDelegate + "<" + rule.ModelType + ">"
            : "global::System.Func<" + rule.ModelType + ", global::System.Threading.Tasks.ValueTask<" + StringSequence + ">>";

        s.Append("    private static readonly ").Append(type).Append(' ').Append(modelKey).Append(rule.Property).AppendLine(" =");
        s.Append("        ").Append(ExtensionsType(entity)).Append('.').Append(RuleMethod(modelKey, rule.Property)).AppendLine(";");
    }

    // The root model and each nested value-object model that has at least one rule, with a prefix that keeps
    // their members apart inside the one rules class.
    private static IEnumerable<RuledModel> RuledModels(Entity entity)
    {
        var root = entity.Members
            .Where(static m => m.RequiredByType || m.ValueObject is { SingleValue: true, Rule: not ValueRule.None })
            .Select(static m => m.RequiredByType
                ? new FieldRule(m.Name, m.ModelType, null, ValueRule.Sync, Required: true)
                : new FieldRule(m.Name, m.ModelType, m.ValueObject!.TypeName, m.ValueObject.Rule, !m.Nullable))
            .ToList();

        if (root.Count > 0)
        {
            yield return new RuledModel(FormModelType(entity), "Root_", root);
        }

        foreach (var nested in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            var rules = nested.Members
                .Where(static m => m.RequiredByType || m.ValueObject is { SingleValue: true, Rule: not ValueRule.None })
                .Select(static m => m.RequiredByType
                    ? new FieldRule(m.Name, m.ModelType, null, ValueRule.Sync, Required: true)
                    : new FieldRule(m.Name, m.ModelType, m.ValueObject!.TypeName, m.ValueObject.Rule, !m.Nullable))
                .ToList();

            if (rules.Count > 0)
            {
                yield return new RuledModel(FormModelType(entity) + "." + nested.ModelName, nested.ModelName + "_", rules);
            }
        }
    }

    private sealed record RuledModel(string Type, string Key, List<FieldRule> Rules);

    /// <summary>One field's rule: its value object's <c>Validate</c>, or — with no value object to ask — only that it is filled.</summary>
    private sealed record FieldRule(string Property, string ModelType, string? ValueObject, ValueRule Kind, bool Required);
}
