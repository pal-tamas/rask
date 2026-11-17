using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Batteries.Generators;

/// <summary>
/// Gives each <c>DbContext</c> a server app declares an <c>IDesignTimeDbContextFactory&lt;T&gt;</c>, so
/// <c>dotnet ef migrations add</c> builds it from configuration — <c>Rask:Database:Provider</c> and
/// <c>Rask:ConnectionStrings:App</c> — instead of starting the app.
/// </summary>
/// <remarks>
/// <para>
/// Without one, <c>dotnet ef</c> runs Program.cs up to <c>Build()</c> to find the context in the container, so a
/// host that cannot start on a developer's machine — a vault it reads, a database it migrates, a check it makes —
/// cannot have migrations made for it either. EF finds a factory by reflection in the context's assembly and the
/// startup assembly, and once it has one it runs nothing else.
/// </para>
/// <para>
/// Not for <c>RaskAppDbContext</c>, a <c>RaskApp</c>'s own context: its model follows the batteries Program.cs
/// switches on and off, which only running Program.cs can tell, so <c>dotnet ef</c> keeps building it from the host.
/// And nothing for a context the app wrote a factory for, or EF would find two.
/// </para>
/// </remarks>
[Generator]
public sealed class DesignTimeFactoryGenerator : IIncrementalGenerator
{
    private const string DbContextName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbContextOptionsName = "Microsoft.EntityFrameworkCore.DbContextOptions";
    private const string FactoryInterfaceName = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";
    private const string RaskAppDbContextName = "Rask.RaskAppDbContext";
    private const string UseRaskDatabaseHost = "Rask.RaskDatabaseDbContextOptionsExtensions";
    private const string AddRaskDataHost = "Rask.Server.RaskDataHostExtensions";
    private const string WebApplicationName = "Microsoft.AspNetCore.Builder.WebApplication";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Only where the generated code compiles and means something: an app on Rask.Server, which owns
        // UseRaskDatabase and WebApplication — and never Rask.Server itself, whose own context is RaskAppDbContext.
        var enabled = context.CompilationProvider.Select(static (compilation, _) =>
            !string.Equals(compilation.AssemblyName, "Rask.Server", StringComparison.Ordinal)
            && compilation.GetTypeByMetadataName(UseRaskDatabaseHost) is not null
            && compilation.GetTypeByMetadataName(WebApplicationName) is not null
            && compilation.GetTypeByMetadataName(FactoryInterfaceName) is not null);

        var classes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, _) => Classify(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        var handWired = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax invocation && IsAddRaskDataCall(invocation),
                static (ctx, _) => HandWiredContext(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        context.RegisterSourceOutput(
            classes.Combine(handWired).Combine(enabled),
            static (spc, input) =>
            {
                if (input.Right)
                {
                    Emit(spc, input.Left.Left, input.Left.Right);
                }
            });
    }

    private static void Emit(
        SourceProductionContext context, ImmutableArray<ClassFact> classes, ImmutableArray<string> providerRequired)
    {
        // A factory the app wrote is the one EF uses; a second one beside it would make EF pick.
        var factored = new HashSet<string>(
            classes.Where(static c => c.Kind == ClassKind.Factory).Select(static c => c.FullyQualifiedName),
            StringComparer.Ordinal);
        var required = new HashSet<string>(providerRequired, StringComparer.Ordinal);

        foreach (var fact in classes
                     .Where(c => c.Kind == ClassKind.Context && c.Context is not null && !factored.Contains(c.FullyQualifiedName))
                     .GroupBy(static c => c.FullyQualifiedName, StringComparer.Ordinal)
                     .Select(static g => g.First())
                     .OrderBy(static c => c.FullyQualifiedName, StringComparer.Ordinal))
        {
            var target = fact.Context!;
            context.AddSource(
                $"__RaskDesignTimeFactory.{HintOf(target.FullyQualifiedName)}.g.cs",
                SourceText.From(DesignTimeFactoryEmitter.Emit(target, required.Contains(target.FullyQualifiedName)), Encoding.UTF8));
        }
    }

    private static string HintOf(string fullyQualifiedName)
    {
        var hint = new StringBuilder();
        foreach (var c in fullyQualifiedName.Replace("global::", string.Empty))
        {
            hint.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');
        }

        return hint.ToString();
    }

    /// <summary>A context this generator could build, a factory the app wrote, or neither (null).</summary>
    private static ClassFact? Classify(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        foreach (var implemented in symbol.AllInterfaces)
        {
            if (implemented.TypeArguments.Length == 1 && Is(implemented, FactoryInterfaceName))
            {
                return new ClassFact(
                    ClassKind.Factory, implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), null);
            }
        }

        return ContextOf(symbol) is { } context ? new ClassFact(ClassKind.Context, context.FullyQualifiedName, context) : null;
    }

    private static DesignTimeContext? ContextOf(INamedTypeSymbol symbol)
    {
        if (symbol.IsAbstract || symbol.IsStatic || symbol.IsFileLocal || !IsReachable(symbol)
            || !Derives(symbol, DbContextName) || Is(symbol, RaskAppDbContextName) || Derives(symbol, RaskAppDbContextName)
            || !symbol.InstanceConstructors.Any(c => TakesOptions(c, symbol)))
        {
            return null;
        }

        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() : string.Empty;
        return new DesignTimeContext(name, ns, symbol.Name, "__Rask" + NestedName(symbol) + "DesignTimeFactory");
    }

    // Generic types cannot be closed over, and a private nested one is out of the factory's reach.
    private static bool IsReachable(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType
                || current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Derives(INamedTypeSymbol symbol, string baseName)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (Is(current, baseName))
            {
                return true;
            }
        }

        return false;
    }

    // The constructor EF's own factory pattern calls: one parameter, the options for this context or for any. One the
    // generated `new` could not call cleanly — obsolete, or leaving required members unset — is no seat for it.
    private static bool TakesOptions(IMethodSymbol constructor, INamedTypeSymbol context)
    {
        if (constructor.Parameters.Length != 1
            || constructor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
            || constructor.Parameters[0].Type is not INamedTypeSymbol parameter
            || HasAttribute(constructor, "System.ObsoleteAttribute")
            || (HasRequiredMembers(context) && !HasAttribute(constructor, "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute")))
        {
            return false;
        }

        return Is(parameter, DbContextOptionsName)
               || (Is(parameter, DbContextOptionsName + "`1")
                   && SymbolEqualityComparer.Default.Equals(parameter.TypeArguments[0], context));
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass is { } type && Is(type, attributeName));

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(static m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return true;
            }
        }

        return false;
    }

    // By metadata name, so a generic type is matched without spelling its type parameter.
    private static bool Is(INamedTypeSymbol type, string metadataName)
    {
        var definition = type.OriginalDefinition;
        return definition.ContainingNamespace is { } ns
               && string.Equals(ns.ToDisplayString() + "." + definition.MetadataName, metadataName, StringComparison.Ordinal);
    }

    private static string NestedName(INamedTypeSymbol symbol) =>
        symbol.ContainingType is { } outer ? NestedName(outer) + "_" + symbol.Name : symbol.Name;

    // Cheap, syntax-only: services.AddRaskData<T>(…). The semantic check below confirms whose method it is.
    private static bool IsAddRaskDataCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax
        {
            Name: GenericNameSyntax { Identifier.ValueText: "AddRaskData", TypeArgumentList.Arguments.Count: 1 },
        };

    /// <summary>
    /// The context an <c>AddRaskData&lt;T&gt;(…)</c> call registers when its callback names the provider itself —
    /// <c>o =&gt; o.UseSqlServer(…)</c> — rather than through <c>UseRaskDatabase</c>; null otherwise.
    /// </summary>
    private static string? HandWiredContext(GeneratorSyntaxContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || method.ContainingType is not { } host || !Is(host, AddRaskDataHost)
            || method.TypeArguments.Length != 1)
        {
            return null;
        }

        var configuresFromSettings = invocation.ArgumentList.Arguments
            .SelectMany(static a => a.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            .Any(static call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "UseRaskDatabase" });

        return configuresFromSettings ? null : method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private enum ClassKind
    {
        Context,
        Factory,
    }

    /// <summary>What one class declaration is to this generator: a context to build, or a factory the app wrote for one.</summary>
    private sealed record ClassFact(ClassKind Kind, string FullyQualifiedName, DesignTimeContext? Context);
}
