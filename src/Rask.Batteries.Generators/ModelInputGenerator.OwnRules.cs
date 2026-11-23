using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// What the app's own half of a generated model asks of it: a `partial class ProductModel : IValidatableObject` (or
// IAsyncValidatableObject), and the validation attributes on that declaration — run by Create(model) and
// Update(id, model) on the server exactly as the form runs them, and the awaited attributes of the model's members too.
public sealed partial class ModelInputGenerator
{
    private const string ValidationContextType = "global::System.ComponentModel.DataAnnotations.ValidationContext";
    private const string AmbientServices = "global::Rask.Ambient.Services";

    /// <summary>The rules the app's own half of a model declares.</summary>
    /// <param name="Validatable">It is an <c>IValidatableObject</c>.</param>
    /// <param name="AwaitsItself">It is an <c>IAsyncValidatableObject</c>, whose <c>ValidateAsync</c> runs in place of <c>Validate</c>.</param>
    /// <param name="TypeChecks">The synchronous validation attributes on its declaration.</param>
    /// <param name="AwaitedTypeChecks">The awaited ones.</param>
    private sealed record ModelOwnRules(
        bool Validatable, bool AwaitsItself, EquatableArray<string> TypeChecks, EquatableArray<string> AwaitedTypeChecks)
    {
        /// <summary>Whether the synchronous pass asks anything of the model itself.</summary>
        public bool Sync => (Validatable && !AwaitsItself) || TypeChecks.Count > 0;

        /// <summary>Whether the awaited pass does.</summary>
        public bool Awaited => AwaitsItself || AwaitedTypeChecks.Count > 0;
    }

    // The model's own declaration, as the app wrote it: the generator's half is not in the compilation it is handed.
    // Read with the aggregate, so an edit to that declaration alone reaches the generated writes on the next full build.
    private static ModelOwnRules? OwnRulesOf(Compilation compilation, INamedTypeSymbol aggregate)
    {
        var name = (aggregate.ContainingNamespace.IsGlobalNamespace ? "" : aggregate.ContainingNamespace.ToDisplayString() + ".") +
                   aggregate.Name + ModelSuffix;
        if (compilation.GetTypeByMetadataName(name) is not { } model)
        {
            return null;
        }

        var faces = model.AllInterfaces.Select(static i => i.ToDisplayString()).ToList();
        var rules = new ModelOwnRules(
            faces.Contains("System.ComponentModel.DataAnnotations.IValidatableObject", StringComparer.Ordinal),
            faces.Contains("System.ComponentModel.DataAnnotations.IAsyncValidatableObject", StringComparer.Ordinal),
            new EquatableArray<string>(model.GetAttributes().Where(IsSyncValidation).Select(ValidationAttributeSource.Construction)),
            new EquatableArray<string>(model.GetAttributes().Where(ValidationAttributeSource.IsAsync).Select(ValidationAttributeSource.Construction)));

        return rules.Sync || rules.Awaited ? rules : null;
    }

    private static bool AwaitsOwnRules(Entity entity) =>
        entity.OwnRules is { Awaited: true } || AwaitedMembers(entity).Any();

    private static IEnumerable<Member> AwaitedMembers(Entity entity) =>
        entity.Members.Where(static m => m.Role == ModelMemberRole.Value && m.Write is not null && m.AwaitedChecks.Count > 0);

    // In the synchronous pass, after the members: the model's own attributes, then its Validate.
    private static void AppendOwnCall(StringBuilder s, Entity entity)
    {
        if (entity.OwnRules is { Sync: true })
        {
            s.AppendLine("        failures = __Own(model, path, failures);");
        }
    }

    // In the awaited pass: each member's awaited attributes — once the synchronous ones let its value through — then
    // the model's own awaited attributes and its ValidateAsync.
    private static void AppendOwnAwaitedCalls(StringBuilder s, Entity entity)
    {
        foreach (var member in AwaitedMembers(entity))
        {
            foreach (var check in member.AwaitedChecks)
            {
                s.Append("        failures = await __CheckAwaited(").Append(check).Append(", model.").Append(member.Name).Append(", \"")
                    .Append(member.Name).Append("\", ").Append(member.Named ?? Quoted(member.Name))
                    .AppendLine(", model, path, failures).ConfigureAwait(false);");
            }
        }

        if (entity.OwnRules is { Awaited: true })
        {
            s.AppendLine("        failures = await __OwnAwaited(model, path, failures).ConfigureAwait(false);");
        }
    }

    private static void AppendOwnMethods(StringBuilder s, Entity entity)
    {
        var model = FormModelType(entity);
        var context = "new " + ValidationContextType + "(model, \"" + entity.Name + ModelSuffix + "\", " + AmbientServices + ", null)";

        if (entity.OwnRules is { Sync: true } own)
        {
            s.Append("    private static ").Append(FailureList).Append(" __Own(").Append(model).Append(" model, string path, ")
                .Append(FailureList).AppendLine(" failures)");
            s.AppendLine("    {");
            s.Append("        var context = ").Append(context).AppendLine(";");
            foreach (var check in own.TypeChecks)
            {
                s.Append("        failures = ").Append(ModelRules).Append(".Said(failures, path, (").Append(check)
                    .AppendLine(").GetValidationResult(model, context));");
            }

            if (own.Validatable && !own.AwaitsItself)
            {
                s.AppendLine("        foreach (var result in ((global::System.ComponentModel.DataAnnotations.IValidatableObject)model).Validate(context))");
                s.AppendLine("        {");
                s.Append("            failures = ").Append(ModelRules).AppendLine(".Said(failures, path, result);");
                s.AppendLine("        }");
            }

            s.AppendLine("        return failures;");
            s.AppendLine("    }");
            s.AppendLine();
        }

        if (entity.OwnRules is { Awaited: true } awaited)
        {
            s.Append("    private static async global::System.Threading.Tasks.Task<").Append(FailureList).Append("> __OwnAwaited(")
                .Append(model).Append(" model, string path, ").Append(FailureList).AppendLine(" failures)");
            s.AppendLine("    {");
            s.Append("        var context = ").Append(context).AppendLine(";");
            foreach (var check in awaited.AwaitedTypeChecks)
            {
                s.Append("        failures = ").Append(ModelRules).Append(".Said(failures, path, await (").Append(check)
                    .AppendLine(").GetValidationResultAsync(model, context, global::Rask.Ambient.CancellationToken).ConfigureAwait(false));");
            }

            if (awaited.AwaitsItself)
            {
                s.AppendLine("        await foreach (var result in ((global::System.ComponentModel.DataAnnotations.IAsyncValidatableObject)model)");
                s.AppendLine("                           .ValidateAsync(context, global::Rask.Ambient.CancellationToken).ConfigureAwait(false))");
                s.AppendLine("        {");
                s.Append("            failures = ").Append(ModelRules).AppendLine(".Said(failures, path, result);");
                s.AppendLine("        }");
            }

            s.AppendLine("        return failures;");
            s.AppendLine("    }");
            s.AppendLine();
        }

        if (AwaitedMembers(entity).Any())
        {
            AppendCheckAwaited(s, entity.ChecksFromCatalog);
        }
    }

    // An awaited attribute on a field, asked only of a value the field's other rules let through: a second lookup for a
    // value already refused is a round trip for an error the reader cannot see until the first is fixed.
    private static void AppendCheckAwaited(StringBuilder s, bool fromCatalog)
    {
        s.Append("    private static async global::System.Threading.Tasks.Task<").Append(FailureList)
            .Append("> __CheckAwaited(global::System.ComponentModel.DataAnnotations.AsyncValidationAttribute check, ")
            .Append("object? value, string name, string displayName, object model, string path, ").Append(FailureList)
            .AppendLine(" failures)");
        s.AppendLine("    {");
        s.Append("        if (").Append(ModelRules).AppendLine(".Names(failures, path + name))");
        s.AppendLine("        {");
        s.AppendLine("            return failures;");
        s.AppendLine("        }");
        s.AppendLine();
        s.Append("        var context = new ").Append(ValidationContextType).Append("(model, displayName, ").Append(AmbientServices)
            .AppendLine(", null) { MemberName = name };");
        s.AppendLine("        var result = await check.GetValidationResultAsync(value, context, global::Rask.Ambient.CancellationToken).ConfigureAwait(false);");
        s.AppendLine("        return result is { ErrorMessage: { } error }");
        s.Append("            ? ").Append(ModelRules).Append(".Told(failures, path, name, ")
            .Append(fromCatalog ? "global::Rask.Core.Forms.DataAnnotationsFieldValidator.Message(check, value, error)" : "error").AppendLine(")");
        s.AppendLine("            : failures;");
        s.AppendLine("    }");
        s.AppendLine();
    }
}
