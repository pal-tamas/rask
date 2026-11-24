using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Generators.Validation;

/// <summary>
///     Registers every <c>AbstractValidator&lt;T&gt;</c> in the compilation, so a <c>Form&lt;T&gt;</c>
///     finds its validator without the author declaring one.
/// </summary>
/// <remarks>
///     <para>
///         Writing the validator IS the registration. There is no <c>AddValidatorsFromAssembly</c> and
///         no scan: the types are found at compile time and registered from a
///         <c>[ModuleInitializer]</c>, which is what lets a WebAssembly app use FluentValidation and
///         still publish trimmed — an assembly scan cannot survive the trimmer, and this never runs one.
///     </para>
///     <para>
///         Ships in every host package's analyzer payload and stays inert unless the app references
///         <c>Rask.Validation.FluentValidation</c>.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ValidatorRegistryGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor RaskVal001 = new(
        "RASKVAL001",
        "Two validators for the same model",
        "'{0}' and '{1}' both validate '{2}' — a model has one validator, so merge them or delete one",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "A form asks for the validator of its model type and gets exactly one. With two, which one ran "
        + "would depend on compilation order, so the rules in the other would silently never run — "
        + "which looks like a validator that does not work rather than one that was never reached. "
        + "Combine the rules into a single AbstractValidator<T>, or use rule sets inside it.",
        DiagnosticHelp.Link("RASKVAL001"));

    private static readonly DiagnosticDescriptor RaskVal002 = new(
        "RASKVAL002",
        "Validator cannot be constructed automatically",
        "'{0}' has no single public constructor, so it was not registered — give it one, or register it "
        + "yourself with RaskValidators.Register",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Registration builds the validator for you: a parameterless constructor is called directly, and "
        + "one taking parameters has them resolved from the render scope. Several public constructors "
        + "leave no way to choose, so nothing is generated. This is a warning rather than an error "
        + "because the validator is still usable by hand — but until it is registered its rules never "
        + "run, and a validator that silently does nothing is the failure worth naming.",
        DiagnosticHelp.Link("RASKVAL002"));

    internal const string ValidatorsStep = "RaskValidators";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Anchored on Rask's own type rather than FluentValidation's: referencing FluentValidation
        // without the Rask integration is a thing an app may legitimately do (a server-side validator
        // it drives itself), and generating registration code against a package that is not there
        // would break that app's build for a feature it never asked for. A bool, so a new compilation
        // that still has both re-runs nothing downstream.
        var enabled = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Rask.RaskValidators") is not null
            && compilation.GetTypeByMetadataName(AbstractValidatorName) is not null);

        // Each validator is read INSIDE its transform, into a value: no symbol, location or compilation crosses
        // into the pipeline, so an edit to anything that is not a validator re-reads nothing.
        var validators = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 } || DeclaresRules(node),
                static (ctx, ct) => Read(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName(ValidatorsStep)
            .Collect();

        context.RegisterSourceOutput(validators.Combine(enabled), static (spc, pair) =>
        {
            if (pair.Right)
            {
                Emit(spc, pair.Left);
            }
        });
    }

    private const string AbstractValidatorName = "FluentValidation.AbstractValidator`1";
    private const string RulesName = "Rules";

    // A class or a record that declares a method named Rules: a candidate for the convention, read below.
    private static bool DeclaresRules(SyntaxNode node) =>
        node is TypeDeclarationSyntax type and (ClassDeclarationSyntax or RecordDeclarationSyntax) &&
        type.Members.OfType<MethodDeclarationSyntax>().Any(static m => string.Equals(m.Identifier.ValueText, RulesName, System.StringComparison.Ordinal));

    /// <summary>One validator, as the registry needs it.</summary>
    /// <param name="TypeFqn">The validator's fully qualified name.</param>
    /// <param name="Name">Its simple name, for diagnostics.</param>
    /// <param name="ModelKey">The model it validates, fully qualified — the registry's key.</param>
    /// <param name="ModelName">The model's simple name, for diagnostics.</param>
    /// <param name="Parameters">The chosen constructor's parameter types, or null when there is no constructor to choose.</param>
    /// <param name="At">Where it is declared.</param>
    /// <param name="Inline">
    ///     For a type's own <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>: whether it is reached directly
    ///     (<see langword="true" />) or, being private, through an <c>[UnsafeAccessor]</c> (<see langword="false" />);
    ///     null for an <c>AbstractValidator&lt;T&gt;</c> class.
    /// </param>
    /// <param name="ValueObject">
    ///     For the <c>Rules</c> of a value object (a record derived from <c>Rask.Values.Value</c>) with no
    ///     <c>Rules()</c> of its own: its namespace, so its generated half can override <c>Rules()</c> to ask them.
    /// </param>
    private sealed record ValidatorModel(
        string TypeFqn,
        string Name,
        string ModelKey,
        string ModelName,
        EquatableArray<string>? Parameters,
        SymbolLocation? At,
        bool? Inline = null,
        string? ValueObject = null);

    private static ValidatorModel? Read(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol type || type.IsGenericType || !IsReachable(type))
        {
            return null;
        }

        return InlineRules(type, ctx.SemanticModel.Compilation) ?? ValidatorClass(type, ctx.SemanticModel.Compilation);
    }

    // A private or protected validator cannot be named by the generated registry, which is a top-level internal class, so
    // Read skips it. Declaring a validator private is itself the statement that nothing outside is to construct it — test
    // fixtures and hand-registered validators live there, and use RaskValidators.Register.
    private static ValidatorModel? ValidatorClass(INamedTypeSymbol type, Compilation compilation)
    {
        if (type.IsAbstract || type.TypeKind != TypeKind.Class || ModelOf(type) is not { } model)
        {
            return null;
        }

        // Through GeneratedModelShape, because a Rask.Data entity's generated model is an error type here:
        // it is generated in this same compilation, by a generator this one cannot see, and an error
        // type displays as the bare name its author wrote. That name neither binds from the registry's
        // global namespace nor keys `ProductModel` and `Shop.ProductModel` as the one model they are.
        var key = GeneratedModelShape.DisplayName(model, SymbolDisplayFormat.FullyQualifiedFormat, compilation);
        var ctor = ChooseConstructor(type);
        return new ValidatorModel(
            Fqn(type),
            type.Name,
            key,
            model.Name,
            ctor is null
                ? null
                : new EquatableArray<string>(ctor.Parameters.Select(static p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))),
            SymbolLocation.From(type.Locations.FirstOrDefault()));
    }

    // The convention: `static void Rules(InlineValidator<T> rules)` on T itself, of any accessibility. Anything else named
    // Rules is RASK139's to point out, and is not registered.
    private static ValidatorModel? InlineRules(INamedTypeSymbol type, Compilation compilation)
    {
        var rules = type.GetMembers(RulesName).OfType<IMethodSymbol>().FirstOrDefault(m =>
            m is { IsStatic: true, ReturnsVoid: true, IsGenericMethod: false, Parameters.Length: 1 } &&
            m.Parameters[0].Type is INamedTypeSymbol { TypeArguments.Length: 1 } parameter &&
            string.Equals(parameter.OriginalDefinition.ToDisplayString(), "FluentValidation.InlineValidator<T>", System.StringComparison.Ordinal) &&
            SymbolEqualityComparer.Default.Equals(parameter.TypeArguments[0], type));
        if (rules is null)
        {
            return null;
        }

        var fqn = Fqn(type);
        return new ValidatorModel(
            fqn,
            type.Name,
            fqn,
            type.Name,
            new EquatableArray<string>([]),
            SymbolLocation.From(rules.Locations.FirstOrDefault()),
            Inline: rules.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal,
            ValueObject: ValueObjectNamespace(type, compilation));
    }

    // A record derived from Rask.Values.Value, top-level and partial, that does not ask Rules() itself (RASK183).
    private static string? ValueObjectNamespace(INamedTypeSymbol type, Compilation compilation)
    {
        var value = compilation.GetTypeByMetadataName("Rask.Values.Value");
        if (value is null || type.ContainingType is not null || !type.IsRecord ||
            type.GetMembers(RulesName).OfType<IMethodSymbol>().Any(static m => m is { IsOverride: true, Parameters.Length: 0 }) ||
            !type.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is TypeDeclarationSyntax t &&
                t.Modifiers.Any(static m => string.Equals(m.ValueText, "partial", System.StringComparison.Ordinal))))
        {
            return null;
        }

        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b, value))
            {
                return type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
            }
        }

        return null;
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<ValidatorModel> validators)
    {
        var found = FindValidators(spc, validators);

        if (found.Count == 0)
        {
            return;
        }

        spc.AddSource("__RaskValidatorRegistry.g.cs", SourceText.From(Build(found), Encoding.UTF8));

        foreach (var valueObject in found.Where(static v => v.ValueObject is not null))
        {
            spc.AddSource(valueObject.Name + ".RaskRules.g.cs", SourceText.From(ValueObjectRules(valueObject), Encoding.UTF8));
        }
    }

    private static List<ValidatorModel> FindValidators(SourceProductionContext spc, ImmutableArray<ValidatorModel> validators)
    {
        var found = new List<ValidatorModel>();
        var byModel = new Dictionary<string, ValidatorModel>(System.StringComparer.Ordinal);

        // A partial validator is reached once per declaration with a base list.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var validator in validators)
        {
            if (!seen.Add(validator.TypeFqn))
            {
                continue;
            }

            // A type's own Rules and one AbstractValidator class run together; two classes are one too many.
            var key = validator.Inline is null ? validator.ModelKey : "rules:" + validator.ModelKey;
            if (byModel.TryGetValue(key, out var existing))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    RaskVal001,
                    validator.At?.ToLocation() ?? Location.None,
                    existing.Name,
                    validator.Name,
                    validator.ModelName));
                continue;
            }

            if (validator.Parameters is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(RaskVal002, validator.At?.ToLocation() ?? Location.None, validator.Name));
                continue;
            }

            byModel[key] = validator;
            found.Add(validator);
        }

        return found;
    }

    // Prefer a parameterless constructor; otherwise there must be exactly one to choose from.
    private static IMethodSymbol? ChooseConstructor(INamedTypeSymbol type)
    {
        var ctors = type.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        return ctors.FirstOrDefault(static c => c.Parameters.Length == 0)
               ?? (ctors.Count == 1 ? ctors[0] : null);
    }

    private static string Build(List<ValidatorModel> found)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskValidatorRegistry");
        sb.AppendLine("{");

        // Keeps each validator's constructor from being trimmed: it is only ever invoked through the
        // generated factory below, which the trimmer cannot see as a use of the type itself.
        foreach (var validator in found.Where(static v => v.Inline is null))
        {
            sb.Append("    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(")
                .Append("global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, ")
                .Append("typeof(").Append(validator.TypeFqn).AppendLine("))]");
        }

        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init() => RefreshAll();");
        sb.AppendLine();
        sb.AppendLine("    internal static void RefreshAll() =>");
        sb.AppendLine("        global::Rask.RaskValidators.Replace(");
        sb.AppendLine("            typeof(__RaskValidatorRegistry),");
        sb.AppendLine("            new global::Rask.ValidatorEntry[]");
        sb.AppendLine("            {");

        for (var i = 0; i < found.Count; i++)
        {
            var validator = found[i];
            if (validator.Inline is { } direct)
            {
                sb.Append("                global::Rask.RaskValidators.Inline<").Append(validator.ModelKey).Append(">(static __rules => ")
                    .Append(direct ? validator.TypeFqn + ".Rules(__rules)" : "Rules_" + Index(i) + "(null, __rules)")
                    .AppendLine("),");
                continue;
            }

            sb.Append("                global::Rask.RaskValidators.Entry<").Append(validator.ModelKey).Append(">(static __sp => new ")
                .Append(validator.TypeFqn).Append('(');
            sb.Append(string.Join(", ", validator.Parameters!.Value.Select(static p =>
                "global::Rask.RaskValidators.Service<" + p + ">(__sp)")));
            sb.AppendLine(")),");
        }

        sb.AppendLine("            });");

        // A private Rules is reached the way a private Validate() is: an accessor, so the type needs no change.
        for (var i = 0; i < found.Count; i++)
        {
            if (found[i].Inline is false)
            {
                sb.AppendLine();
                sb.AppendLine("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.StaticMethod, Name = \"Rules\")]");
                sb.Append("    private static extern void Rules_").Append(Index(i)).Append('(')
                    .Append(found[i].TypeFqn).Append("? _, global::FluentValidation.InlineValidator<").Append(found[i].TypeFqn).AppendLine("> rules);");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Index(int i) => i.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // A value object's half that asks its FluentValidation rules as it is made, after its base's own: what Value's
    // constructor calls Rules() for.
    private static string ValueObjectRules(ValidatorModel valueObject)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (valueObject.ValueObject is { Length: > 0 } ns)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial record ").AppendLine(valueObject.Name);
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>What its <c>static Rules(InlineValidator&lt;T&gt;)</c> says against it.</summary>");
        sb.AppendLine("    /// <returns>One problem per broken rule.</returns>");
        sb.AppendLine("    protected override global::System.Collections.Generic.IEnumerable<global::Rask.Values.Problem> Rules() =>");
        sb.AppendLine("        global::Rask.RaskValidators.Problems(this);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // Whether a top-level internal class in this same assembly can name the type: it and every type it
    // is nested in must be public or internal.
    private static bool IsReachable(INamedTypeSymbol type)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Fqn(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // The T of the nearest AbstractValidator<T> in the base chain, or null when there is none.
    private static ITypeSymbol? ModelOf(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.TypeArguments.Length == 1
                && string.Equals(b.OriginalDefinition.MetadataName, "AbstractValidator`1", System.StringComparison.Ordinal)
                && string.Equals(b.OriginalDefinition.ContainingNamespace?.ToDisplayString(), "FluentValidation", System.StringComparison.Ordinal))
            {
                return b.TypeArguments[0];
            }
        }

        return null;
    }
}
