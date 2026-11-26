using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK133 — app code writes an aggregate on a <c>DbContext</c> and saves it there:
///     <c>db.Products.Add(product)</c>, <c>db.Update(order)</c> or <c>db.Remove(supply)</c>, then
///     <c>db.SaveChangesAsync()</c>, around the aggregate's factory, operations and the rules every save asks.
/// </summary>
/// <remarks>
///     <para>
///         Reported where the write is, when the same method (lambdas included) also calls <c>SaveChanges</c> or
///         <c>SaveChangesAsync</c>: a context that only stages a write for its caller to save, as a generated
///         write's <c>db:</c> does, is not. Not reported in a seed (a method or a type whose name says <c>Seed</c>,
///         the work of <c>UseSeeding</c>/<c>UseAsyncSeeding</c>), a migration (a <c>Migrations</c> folder, a
///         <c>Migration</c> or <c>ModelSnapshot</c>), a test (a <c>*.Tests</c> assembly, a test attribute) or generated
///         code — Rask's own generated entries among it. Reported inside <c>Db.Transaction(…)</c> too: an entry
///         called there joins the block's transaction, so the rewrite is as atomic as the write it replaces.
///     </para>
///     <para>
///         <see cref="FactoryProperty" /> is set when the rewrite is mechanical: <c>db.Add(X.F(…));</c> followed at once by
///         <c>await db.SaveChangesAsync(…);</c> on a context local that nothing else in the method touches, where
///         <c>F</c> is a factory with a generated entry. The additional locations are then the factory call, the save
///         statement and the context's declaration, and <see cref="TokenProperty" /> adds the save's token.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextWriteAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The factory's name, set when the write and its save become the factory's generated entry.</summary>
    public const string FactoryProperty = "Factory";

    /// <summary>Set ("1") when the save's cancellation token is the last additional location.</summary>
    public const string TokenProperty = "Token";

    private static readonly DiagnosticDescriptor Rask133 = new(
        "RASK133",
        "An aggregate is written on a DbContext around its own operations",
        "'{0}' writes the aggregate '{1}' on a DbContext that saves it, past its factory, its operations and the rules every save asks — {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An aggregate is the transaction boundary: its generated entries load it whole, run its factory or "
                     + "operation, ask its roles and its rules and save it in one transaction. DbSet.Add/Update/Remove "
                     + "or DbContext.Add/Update/Remove followed by SaveChanges writes it around all of that. Call the "
                     + "generated entry (X.Register(…), X.M(id, version, …), X.Delete(id, version)), or Find, the "
                     + "operation and Save() inside Db.Transaction. Inside Db.Transaction(…) a generated entry joins "
                     + "that transaction — the same connection, committed or rolled back with everything else the work "
                     + "writes — so the rewrite keeps a load of several writes atomic. Seeds, migrations, tests and "
                     + "generated code are not reported. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK133"));

    private static readonly ImmutableHashSet<string> AddNames = ImmutableHashSet.Create(
        StringComparer.Ordinal, "Add", "AddAsync", "AddRange", "AddRangeAsync");

    private static readonly ImmutableHashSet<string> UpdateNames = ImmutableHashSet.Create(
        StringComparer.Ordinal, "Update", "UpdateRange");

    private static readonly ImmutableHashSet<string> RemoveNames = ImmutableHashSet.Create(
        StringComparer.Ordinal, "Remove", "RemoveRange");

    private static readonly ImmutableHashSet<string> TestAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal, "Fact", "Theory", "Test", "TestMethod", "DataTestMethod", "TestCase", "TestCaseSource",
        "TestClass", "TestFixture");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask133);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext") is not { } dbContext ||
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1") is not { } dbSet ||
                IsTestAssembly(start.Compilation.AssemblyName))
            {
                return;
            }

            var known = new KnownTypes(
                dbContext,
                dbSet,
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.Migration"),
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot"));
            start.RegisterOperationBlockAction(block => Analyze(block, known));
        });
    }

    private static void Analyze(OperationBlockAnalysisContext context, KnownTypes known)
    {
        if (IsExempt(context.OwningSymbol, context.OperationBlocks, known))
        {
            return;
        }

        var calls = context.OperationBlocks
            .SelectMany(static block => block.DescendantsAndSelf())
            .OfType<IInvocationOperation>()
            .ToList();
        if (!calls.Exists(call => IsSave(call, known)))
        {
            return;
        }

        foreach (var call in calls)
        {
            if (Verb(call.TargetMethod.Name) is not { } verb ||
                WrittenAggregate(call, known) is not { } aggregate ||
                InSeeding(call))
            {
                continue;
            }

            var properties = ImmutableDictionary<string, string?>.Empty;
            var additional = new List<Location>();
            var factory = verb == WriteVerb.Add ? FactoryOf(aggregate) : null;
            if (Mechanical(call, aggregate, calls, known, context.OperationBlocks, context.CancellationToken) is { } fix)
            {
                properties = properties.Add(FactoryProperty, fix.Factory.TargetMethod.Name);
                additional.Add(fix.Factory.Syntax.GetLocation());
                additional.Add(fix.Save.GetLocation());
                additional.Add(fix.Declaration.GetLocation());
                if (fix.Token is not null)
                {
                    additional.Add(fix.Token.GetLocation());
                    properties = properties.Add(TokenProperty, "1");
                }

                factory = fix.Factory.TargetMethod.Name;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask133, call.Syntax.GetLocation(), additional, properties,
                call.TargetMethod.Name, aggregate.Name, Advice(verb, aggregate, factory)));
        }
    }

    private enum WriteVerb
    {
        Add,
        Update,
        Remove,
    }

    private static WriteVerb? Verb(string name)
    {
        if (AddNames.Contains(name))
        {
            return WriteVerb.Add;
        }

        if (UpdateNames.Contains(name))
        {
            return WriteVerb.Update;
        }

        return RemoveNames.Contains(name) ? WriteVerb.Remove : null;
    }

    private static string Advice(WriteVerb verb, INamedTypeSymbol aggregate, string? factory)
    {
        var name = aggregate.Name;
        var version = AggregateShape.HasVersion(aggregate) ? "id, version" : "id";
        return verb switch
        {
            WriteVerb.Add when factory is not null => "call its factory's generated entry '" + name + "." + factory + "(…)'",
            WriteVerb.Add => "build it with its factory and call the factory's generated entry ('" + name + ".Register(…)'), or '" + name + ".Create(model)'",
            WriteVerb.Update => "call the operation's generated entry '" + name + ".M(" + version + ", …)', or Find it, run the operation and Save() inside Db.Transaction",
            _ => "call '" + name + ".Delete(" + version + ")', or Find it and Delete() it inside Db.Transaction",
        };
    }

    // `db.SaveChanges()` / `await db.SaveChangesAsync(…)` on any DbContext.
    private static bool IsSave(IInvocationOperation call, KnownTypes known) =>
        call.TargetMethod.Name is "SaveChanges" or "SaveChangesAsync" &&
        DerivesFrom(call.Instance?.Type ?? call.TargetMethod.ContainingType, known.DbContext);

    // The aggregate a DbSet<T> or DbContext write hands over, or null for a write of something else.
    private static INamedTypeSymbol? WrittenAggregate(IInvocationOperation call, KnownTypes known)
    {
        var method = call.TargetMethod;
        if (method.ContainingType is { } set && SymbolEqualityComparer.Default.Equals(set.OriginalDefinition, known.DbSet))
        {
            return set.TypeArguments[0] is INamedTypeSymbol entity && AggregateShape.IsAggregate(entity) ? entity : null;
        }

        if (!DerivesFrom(method.ContainingType, known.DbContext))
        {
            return null;
        }

        if (method.IsGenericMethod)
        {
            return method.TypeArguments[0] is INamedTypeSymbol generic && AggregateShape.IsAggregate(generic) ? generic : null;
        }

        return call.Arguments
            .SelectMany(static argument => argument.Value is IArrayCreationOperation { Initializer: { } initializer }
                ? initializer.ElementValues
                : [argument.Value])
            .Select(static value => (value is IConversionOperation { Operand: var operand } ? operand : value).Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(AggregateShape.IsAggregate);
    }

    // The work of `options.UseSeeding(…)` / `UseAsyncSeeding(…)`: EF's own seed.
    private static bool InSeeding(IOperation call)
    {
        for (var current = call.Parent; current is not null; current = current.Parent)
        {
            if (current is IInvocationOperation { TargetMethod.Name: "UseSeeding" or "UseAsyncSeeding" })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExempt(ISymbol owner, ImmutableArray<IOperation> blocks, KnownTypes known)
    {
        if (blocks.Any(static block => block.Syntax.SyntaxTree.FilePath.Replace('\\', '/').Split('/')
                .Any(static segment => string.Equals(segment, "Migrations", StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        for (var symbol = owner; symbol is not null and not INamespaceSymbol; symbol = symbol.ContainingSymbol)
        {
            if (symbol.Name.IndexOf("Seed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                symbol.GetAttributes().Any(static a => a.AttributeClass is { } type && TestAttributes.Contains(Trimmed(type.Name))) ||
                (symbol is INamedTypeSymbol type && (DerivesFrom(type, known.Migration) || DerivesFrom(type, known.ModelSnapshot))))
            {
                return true;
            }
        }

        return false;
    }

    private static string Trimmed(string attribute) =>
        attribute.EndsWith("Attribute", StringComparison.Ordinal) ? attribute.Substring(0, attribute.Length - "Attribute".Length) : attribute;

    // `Shop.Tests`, `Shop.IntegrationTests`, `Shop.Specs`: a test project, which writes its fixtures as it likes.
    private static bool IsTestAssembly(string? name)
    {
        var last = name?.Split('.').LastOrDefault() ?? "";
        return last.EndsWith("Tests", StringComparison.Ordinal) || last.EndsWith("Specs", StringComparison.Ordinal);
    }

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
    {
        for (var current = type as INamedTypeSymbol; current is not null && baseType is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    // The aggregate's one factory with a generated entry, by name, or null when it has none or several.
    private static string? FactoryOf(INamedTypeSymbol aggregate)
    {
        var names = aggregate.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.IsStatic && !OperationEntryCallAnalyzer.IsEntry(method) &&
                             SymbolEqualityComparer.Default.Equals(method.ReturnType, aggregate) && EntryOf(aggregate, method) is not null)
            .Select(static method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count == 1 ? names[0] : null;
    }

    // The generated entry of a factory: a static of the same name taking the factory's parameters and a token.
    private static IMethodSymbol? EntryOf(INamedTypeSymbol aggregate, IMethodSymbol factory) =>
        aggregate.GetMembers(factory.Name)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(entry =>
                entry.IsStatic &&
                OperationEntryCallAnalyzer.IsEntry(entry) &&
                entry.Parameters.Length == factory.Parameters.Length + 1 &&
                string.Equals(entry.Parameters[entry.Parameters.Length - 1].Name, "cancellationToken", StringComparison.Ordinal) &&
                factory.Parameters
                    .Select((parameter, i) => SymbolEqualityComparer.Default.Equals(parameter.Type, entry.Parameters[i].Type))
                    .All(static same => same));

    private sealed record Fix(IInvocationOperation Factory, StatementSyntax Save, LocalDeclarationStatementSyntax Declaration, ExpressionSyntax? Token);

    // `db.Add(X.F(…)); await db.SaveChangesAsync(…);` on a context local the method uses for nothing else: the two
    // statements are the factory's entry, and the context can go.
    private static Fix? Mechanical(
        IInvocationOperation write, INamedTypeSymbol aggregate, List<IInvocationOperation> calls, KnownTypes known, ImmutableArray<IOperation> blocks,
        System.Threading.CancellationToken cancellation)
    {
        if (!string.Equals(write.TargetMethod.Name, "Add", StringComparison.Ordinal) ||
            write.Parent is not IExpressionStatementOperation { Syntax: StatementSyntax written } ||
            written.Parent is not BlockSyntax block ||
            write.Arguments.Length != 1 ||
            Unwrapped(write.Arguments[0].Value) is not IInvocationOperation { Instance: null, Syntax: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax } } factory ||
            !SymbolEqualityComparer.Default.Equals(factory.TargetMethod.ContainingType, aggregate) ||
            !SymbolEqualityComparer.Default.Equals(factory.TargetMethod.ReturnType, aggregate) ||
            EntryOf(aggregate, factory.TargetMethod) is null ||
            ContextLocal(write.Instance, known) is not { } context)
        {
            return null;
        }

        var index = block.Statements.IndexOf(written);
        if (index + 1 >= block.Statements.Count ||
            block.Statements[index + 1] is not ExpressionStatementSyntax { Expression: AwaitExpressionSyntax } saveStatement ||
            calls.Find(call => call.Syntax.Parent == ((AwaitExpressionSyntax)saveStatement.Expression)) is not { } save ||
            !IsSave(save, known) || !string.Equals(save.TargetMethod.Name, "SaveChangesAsync", StringComparison.Ordinal) ||
            !SymbolEqualityComparer.Default.Equals(ContextLocal(save.Instance, known), context) ||
            context.DeclaringSyntaxReferences.Length != 1 ||
            context.DeclaringSyntaxReferences[0].GetSyntax(cancellation) is not VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax { Variables.Count: 1, Parent: LocalDeclarationStatementSyntax declaration },
            } ||
            declaration.Parent != block)
        {
            return null;
        }

        // Nothing but the write and the save reads the context, so nothing else was staged on it.
        var uses = blocks
            .SelectMany(static b => b.DescendantsAndSelf())
            .OfType<ILocalReferenceOperation>()
            .Count(reference => SymbolEqualityComparer.Default.Equals(reference.Local, context));
        if (uses != 2)
        {
            return null;
        }

        var token = save.Arguments.FirstOrDefault(static a =>
            a.ArgumentKind == ArgumentKind.Explicit && string.Equals(a.Parameter?.Name, "cancellationToken", StringComparison.Ordinal));
        return save.Arguments.Any(static a => a.ArgumentKind == ArgumentKind.Explicit && !string.Equals(a.Parameter?.Name, "cancellationToken", StringComparison.Ordinal))
            ? null
            : new Fix(factory, saveStatement, declaration, token?.Value.Syntax as ExpressionSyntax);
    }

    // `db` in `db.Add(…)` or `db.Products.Add(…)`: the context local a write or a save goes through.
    private static ILocalSymbol? ContextLocal(IOperation? instance, KnownTypes known) =>
        instance switch
        {
            ILocalReferenceOperation { Local: var local } when DerivesFrom(local.Type, known.DbContext) => local,
            IPropertyReferenceOperation { Instance: ILocalReferenceOperation { Local: var local } } when DerivesFrom(local.Type, known.DbContext) => local,
            _ => null,
        };

    private static IOperation Unwrapped(IOperation value) =>
        value is IConversionOperation { IsImplicit: true, Operand: var operand } ? operand : value;

    private sealed record KnownTypes(INamedTypeSymbol DbContext, INamedTypeSymbol DbSet, INamedTypeSymbol? Migration, INamedTypeSymbol? ModelSnapshot);
}
