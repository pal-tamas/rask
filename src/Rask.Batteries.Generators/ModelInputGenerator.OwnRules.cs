using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// What the app's own half of a generated model asks of it: a `partial class ProductModel : IValidatableObject`, and the
// validation attributes on that declaration — run by Create(model) and Update(id, model) on the server, after the
// fields, exactly as the form runs them.
public sealed partial class ModelInputGenerator
{
    private const string ValidationContextType = "global::System.ComponentModel.DataAnnotations.ValidationContext";
    private const string AmbientServices = "global::Rask.Ambient.Services";

    /// <summary>The rules the app's own half of a model declares.</summary>
    /// <param name="Validatable">
    ///     It is an <c>IValidatableObject</c> — and not an <c>IAsyncValidatableObject</c>, whose <c>ValidateAsync</c> .NET
    ///     runs in its place and only the form awaits.
    /// </param>
    /// <param name="TypeChecks">The synchronous validation attributes on its declaration.</param>
    private sealed record ModelOwnRules(bool Validatable, EquatableArray<string> TypeChecks);

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
            faces.Contains("System.ComponentModel.DataAnnotations.IValidatableObject", StringComparer.Ordinal) &&
            !faces.Contains("System.ComponentModel.DataAnnotations.IAsyncValidatableObject", StringComparer.Ordinal),
            new EquatableArray<string>(model.GetAttributes().Where(IsSyncValidation).Select(ValidationAttributeSource.Construction)));

        return rules.Validatable || rules.TypeChecks.Count > 0 ? rules : null;
    }

    // After the members: the model's own attributes, then its Validate.
    private static void AppendOwnCall(StringBuilder s, Entity entity)
    {
        if (entity.OwnRules is not null)
        {
            s.AppendLine("        failures = __Own(model, path, failures);");
        }
    }

    private static void AppendOwnMethods(StringBuilder s, Entity entity)
    {
        if (entity.OwnRules is not { } own)
        {
            return;
        }

        s.Append("    private static ").Append(FailureList).Append(" __Own(").Append(FormModelType(entity)).Append(" model, string path, ")
            .Append(FailureList).AppendLine(" failures)");
        s.AppendLine("    {");
        s.Append("        var context = new ").Append(ValidationContextType).Append("(model, \"").Append(entity.Name).Append(ModelSuffix)
            .Append("\", ").Append(AmbientServices).AppendLine(", null);");
        foreach (var check in own.TypeChecks)
        {
            s.Append("        failures = ").Append(ModelRules).Append(".Said(failures, path, (").Append(check)
                .AppendLine(").GetValidationResult(model, context));");
        }

        if (own.Validatable)
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
}
