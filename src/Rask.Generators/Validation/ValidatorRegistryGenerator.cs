using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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
///         <c>Rask.Validation.FluentValidation</c>, the same way <c>BlazorGenerator</c> stays inert
///         without <c>Rask.Blazor</c>.
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

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Straight off the compilation, matching BlazorGenerator and CqrsCodecGenerator: symbols must
        // not be held across an incremental boundary.
        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => Emit(spc, compilation));
    }

    private static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        // Anchored on Rask's own type rather than FluentValidation's: referencing FluentValidation
        // without the Rask integration is a thing an app may legitimately do (a server-side validator
        // it drives itself), and generating registration code against a package that is not there
        // would break that app's build for a feature it never asked for.
        if (compilation.GetTypeByMetadataName("Rask.Validation.FluentValidation.RaskValidators") is null)
        {
            return;
        }

        if (compilation.GetTypeByMetadataName("FluentValidation.AbstractValidator`1") is not { } abstractValidator)
        {
            return;
        }

        var found = new List<(INamedTypeSymbol Validator, ITypeSymbol Model, IMethodSymbol? Ctor)>();
        var byModel = new Dictionary<string, INamedTypeSymbol>(System.StringComparer.Ordinal);

        foreach (var type in Types(compilation.Assembly.GlobalNamespace))
        {
            if (type.IsAbstract || type.TypeKind != TypeKind.Class || type.IsGenericType)
            {
                continue;
            }

            if (ModelOf(type, abstractValidator) is not { } model)
            {
                continue;
            }

            // A private or protected validator cannot be named by the generated registry, which is a
            // top-level internal class. Skipping it silently is right rather than lax: declaring a
            // validator private is itself the statement that nothing outside is to construct it, and
            // the alternative — emitting `new Outer.PrivateValidator()` — is a CS0122 in generated code
            // the author never wrote. Test fixtures and hand-registered validators live here; they use
            // RaskValidators.Register.
            if (!IsReachable(type))
            {
                continue;
            }

            var key = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (byModel.TryGetValue(key, out var existing))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    RaskVal001,
                    type.Locations.FirstOrDefault(),
                    existing.Name,
                    type.Name,
                    model.Name));
                continue;
            }

            var ctors = type.InstanceConstructors
                .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
                .ToList();

            // Prefer a parameterless constructor; otherwise there must be exactly one to choose from.
            var ctor = ctors.FirstOrDefault(static c => c.Parameters.Length == 0)
                       ?? (ctors.Count == 1 ? ctors[0] : null);

            if (ctor is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(RaskVal002, type.Locations.FirstOrDefault(), type.Name));
                continue;
            }

            byModel[key] = type;
            found.Add((type, model, ctor));
        }

        if (found.Count == 0)
        {
            return;
        }

        spc.AddSource("__RaskValidatorRegistry.g.cs", SourceText.From(Build(found), Encoding.UTF8));
    }

    private static string Build(List<(INamedTypeSymbol Validator, ITypeSymbol Model, IMethodSymbol? Ctor)> found)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskValidatorRegistry");
        sb.AppendLine("{");

        // Keeps each validator's constructor from being trimmed: it is only ever invoked through the
        // generated factory below, which the trimmer cannot see as a use of the type itself.
        foreach (var (validator, _, _) in found)
        {
            sb.Append("    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(")
                .Append("global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, ")
                .Append("typeof(").Append(Fqn(validator)).AppendLine("))]");
        }

        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init() => RefreshAll();");
        sb.AppendLine();
        sb.AppendLine("    internal static void RefreshAll() =>");
        sb.AppendLine("        global::Rask.Validation.FluentValidation.RaskValidators.Replace(");
        sb.AppendLine("            typeof(__RaskValidatorRegistry),");
        sb.AppendLine("            new (global::System.Type, global::System.Func<global::System.IServiceProvider?, object>)[]");
        sb.AppendLine("            {");

        foreach (var (validator, model, ctor) in found)
        {
            sb.Append("                (typeof(").Append(Fqn(model)).Append("), static __sp => new ")
                .Append(Fqn(validator)).Append('(');
            sb.Append(string.Join(", ", ctor!.Parameters.Select(static p =>
                "global::Rask.Validation.FluentValidation.RaskValidators.Service<"
                + p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">(__sp)")));
            sb.AppendLine(")),");
        }

        sb.AppendLine("            });");
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
    private static ITypeSymbol? ModelOf(INamedTypeSymbol type, INamedTypeSymbol abstractValidator)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, abstractValidator)
                && b.TypeArguments.Length == 1)
            {
                return b.TypeArguments[0];
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (var t in Types(ns))
                    {
                        yield return t;
                    }

                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in Types(type))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }
}
