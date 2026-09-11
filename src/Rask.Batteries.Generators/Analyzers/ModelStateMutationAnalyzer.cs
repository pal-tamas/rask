using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Data.Generators.Analyzers;

/// <summary>
///     RASK080 — an entity or a value object whose state can be changed from outside the type.
/// </summary>
/// <remarks>
///     <para>
///         The generated form model is where a user's input lands, and it writes the entity through its
///         private setters. That makes a public setter on the entity pure surface: nothing in the framework
///         needs it, and every caller that uses it goes around the methods that keep the entity valid. So the
///         entity itself is the only thing that should change its state.
///     </para>
///     <para>
///         Symbol-based rather than syntax-based, so a partial type is judged once as a whole and members
///         inherited from metadata (<c>Model&lt;TId&gt;.Id</c>, a protected setter) are never seen at all —
///         <see cref="INamespaceOrTypeSymbol.GetMembers()" /> is the type's OWN members.
///     </para>
///     <para>
///         The one exemption is the property a positional record parameter declares:
///         <c>record Money(decimal Amount, string Currency) : IValueObject</c> gets public <c>init</c>
///         accessors from the compiler, and that is the idiomatic immutable value object — there is no
///         accessor in the source to change. It is recognised by its declaring syntax being the
///         <see cref="ParameterSyntax" /> rather than a property declaration. A positional parameter of a
///         non-readonly <c>record struct</c> gets a real <c>set</c>, which is still reported, at the parameter.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModelStateMutationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask080 = new(
        "RASK080",
        "Model state can be changed from outside the type",
        "'{0}.{1}' {2}, so code outside '{0}' can change its state — {3}, and change it through the methods of "
        + "'{0}' (or its constructor)",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An entity (a class deriving from Rask.Data.Model) or a value object (IValueObject) should "
                     + "be changed only by itself. A public setter, a public init accessor or a public mutable "
                     + "field lets any caller skip the methods that keep it valid. The generated form model "
                     + "writes through private setters, so nothing in the framework needs the public one. A "
                     + "positional record parameter's compiler-generated init accessor is exempt.",
        helpLinkUri: DiagnosticHelp.Link("RASK080"));

    private static readonly ImmutableDictionary<string, string?> NoFix =
        ImmutableDictionary<string, string?>.Empty.Add(ModelTypes.NoFixProperty, "true");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask080);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var types = ModelTypes.Resolve(start.Compilation);
            if (types.Model is null && types.ValueObject is null)
            {
                return;
            }

            start.RegisterSymbolAction(ctx => Analyze(ctx, types), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ModelTypes types)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!types.IsEntity(type) && !types.IsValueObject(type))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol property:
                    AnalyzeProperty(context, type, property);
                    break;
                case IFieldSymbol field:
                    AnalyzeField(context, type, field);
                    break;
            }
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, INamedTypeSymbol type, IPropertySymbol property)
    {
        // An override cannot narrow what it overrides (CS0507); the base declaration is where the choice was
        // made, and it is reported there when it is in source.
        if (property.IsStatic || property.IsOverride
                              || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public } setter)
        {
            return;
        }

        var declaration = property.DeclaringSyntaxReferences.Length > 0
            ? property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken)
            : null;

        if (declaration is ParameterSyntax parameter)
        {
            // The compiler-generated property of a positional record parameter. Its init accessor is the
            // immutable value object idiom; a non-readonly record struct's set is not.
            if (!setter.IsInitOnly)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask080, parameter.GetLocation(), NoFix, type.Name, property.Name,
                    "has a public setter (a positional parameter of a record struct that is not readonly)",
                    "declare it as a 'readonly record struct'"));
            }

            return;
        }

        if (setter.DeclaringSyntaxReferences.Length == 0
            || setter.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken)
                is not AccessorDeclarationSyntax accessor)
        {
            return;
        }

        var keyword = setter.IsInitOnly ? "init" : "set";
        context.ReportDiagnostic(Diagnostic.Create(
            Rask080,
            accessor.GetLocation(),
            CanNarrow(type, property, setter, accessor) ? null : NoFix,
            type.Name,
            property.Name,
            setter.IsInitOnly ? "has a public init accessor" : "has a public setter",
            $"make it 'private {keyword}'"));
    }

    private static void AnalyzeField(SymbolAnalysisContext context, INamedTypeSymbol type, IFieldSymbol field)
    {
        if (field.IsStatic || field.IsConst || field.IsReadOnly || field.IsImplicitlyDeclared
            || field.DeclaredAccessibility != Accessibility.Public
            || field.DeclaringSyntaxReferences.Length == 0
            || field.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken)
                is not VariableDeclaratorSyntax declarator)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask080,
            declarator.GetLocation(),
            field.IsRequired ? NoFix : null,
            type.Name,
            field.Name,
            "is a public field that is not readonly",
            "make it private"));
    }

    // Whether `private set` / `private init` compiles in place of the public accessor. Each case below is a
    // rule the narrowed accessor would break, so the warning stands but no lightbulb offers a broken edit.
    private static bool CanNarrow(
        INamedTypeSymbol type, IPropertySymbol property, IMethodSymbol setter, AccessorDeclarationSyntax accessor)
    {
        // CS9032: a required member's setter cannot be less visible than its type. CS0442: an abstract
        // accessor cannot be private. CS0276: an accessor modifier needs a second accessor to differ from, so
        // a set-only property (`{ set => _hash = Hash(value); }`) cannot take `private set`.
        if (property.IsRequired || property.IsAbstract || property.GetMethod is null)
        {
            return false;
        }

        // CS0274: only one accessor may carry a modifier.
        if (accessor.Parent is AccessorListSyntax list)
        {
            foreach (var other in list.Accessors)
            {
                if (other != accessor && other.Modifiers.Count > 0)
                {
                    return false;
                }
            }
        }

        // CS0737: an interface that demands a public setter is implemented by this one.
        foreach (var implemented in type.AllInterfaces)
        {
            foreach (var candidate in implemented.GetMembers(property.Name))
            {
                if (candidate is IPropertySymbol { SetMethod: { } required }
                    && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(required), setter))
                {
                    return false;
                }
            }
        }

        return !accessor.Modifiers.Any(SyntaxKind.PrivateKeyword);
    }
}
