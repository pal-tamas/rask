using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK177 — app code written for one database: an API of the SQL Server, PostgreSQL or SQLite provider (or of
///     their ADO.NET drivers), <c>EF.Functions.*</c>, raw SQL (<c>FromSql*</c>, <c>ExecuteSql*</c>, <c>SqlQuery*</c>),
///     or SQL in the model (<c>HasColumnType</c>, <c>UseCollation</c>, <c>HasDefaultValueSql</c>,
///     <c>HasComputedColumnSql</c>, <c>HasFilter</c>, <c>ToSqlQuery</c>).
/// </summary>
/// <remarks>
///     <para>
///         An app's tests run on SQLite in memory, and Rask proves each thing it hides behaves the same on SQLite, SQL
///         Server and PostgreSQL. Code written for one database is outside that proof: it passes on the database the
///         tests run on and differs on the one production runs on. The message names the provider-neutral way.
///     </para>
///     <para>
///         Not reported: a migration (a type derived from <c>Migration</c> or <c>ModelSnapshot</c>, or a file under a
///         <c>Migrations</c> folder) and generated code, which are written for the database they migrate; and the call
///         that selects the provider (<c>UseSqlServer</c>, <c>UseAzureSql</c>, <c>UseAzureSynapse</c>,
///         <c>UseNpgsql</c>, <c>UseSqlite</c> on a <c>DbContextOptionsBuilder</c>), with everything in its arguments —
///         the one place an app may name its database. Rask's own packages are compiled apart from the app, so their
///         provider code is never seen. A suggestion by default; a warning under <c>&lt;RaskStrict&gt;</c>, an error at
///         errors. Reported only when the compilation has Rask.Data, which holds every replacement named.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProviderSpecificCodeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask177 = new(
        "RASK177",
        "Code written for one database",
        "'{0}' is written for {1}, which the app's SQLite tests do not prove — {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An app holds no code for one database: Rask translates the model, the queries, text order, "
                     + "locks, durations, covering and filtered indexes, computed columns, defaults and constraint "
                     + "violations for SQLite, SQL Server and PostgreSQL alike, and proves each on all three — which is "
                     + "what makes a test on SQLite in memory hold on the database production runs. A provider's API, "
                     + "EF.Functions, raw SQL and SQL in the model are outside that proof. Migrations, generated code "
                     + "and the call that selects the provider are not reported. A suggestion by default; a warning "
                     + "under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK177"));

    private const string Neutral = "write it once for every database through Rask.Data, and let UseRaskDatabase(sp) pick the provider";

    private static readonly ImmutableHashSet<string> ProviderSelection = ImmutableHashSet.Create(
        StringComparer.Ordinal, "UseSqlServer", "UseAzureSql", "UseAzureSynapse", "UseNpgsql", "UseSqlite");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask177);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Data.Db") is null
                || start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbFunctions") is not { } functions)
            {
                return;
            }

            var known = new KnownTypes(
                functions,
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder"),
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.Migration"),
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot"),
                start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder"));

            start.RegisterOperationAction(
                operation => AnalyzeMember(operation, known),
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.MethodReference,
                OperationKind.EventReference);
            start.RegisterSyntaxNodeAction(
                node => AnalyzeType(node, known),
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName);
        });
    }

    private static void AnalyzeMember(OperationAnalysisContext context, KnownTypes known)
    {
        var (member, at) = context.Operation switch
        {
            IInvocationOperation call => ((ISymbol)(call.TargetMethod.ReducedFrom ?? call.TargetMethod), NameOf(call.Syntax)),
            IMemberReferenceOperation reference => (reference.Member, NameOf(reference.Syntax)),
            _ => (null, null),
        };

        if (member is null || at is null)
        {
            return;
        }

        // Classified first, exempted after: the exemption walks the call's ancestors, which only a finding pays for.
        var finding = (context.Operation as IInvocationOperation) is { } invocation ? Replacement(invocation, known) : null;
        if (finding is null && ProviderOf(member.ContainingAssembly) is { } provider)
        {
            finding = (Shown(member), provider, ProviderReplacement(member.Name));
        }

        if (finding is var (name, database, replacement)
            && !IsExempt(context.Operation.Syntax, context.Operation.SemanticModel, context.ContainingSymbol, known, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask177, at.GetLocation(), name, database, replacement));
        }
    }

    private static string Shown(ISymbol member) =>
        member.IsStatic && member is not IMethodSymbol { IsExtensionMethod: true }
            ? member.ContainingType.Name + "." + member.Name
            : member.Name;

    private static void AnalyzeType(SyntaxNodeAnalysisContext context, KnownTypes known)
    {
        var name = (SimpleNameSyntax)context.Node;
        if (name.IsVar
            || name.Parent is QualifiedNameSyntax { Left: var left } && left == name
            || name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null
            || !SyntaxFacts.IsInTypeOnlyContext(name)
            || context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is not INamedTypeSymbol type
            || ProviderOf(type.ContainingAssembly) is not { } provider
            || IsExempt(name, context.SemanticModel, context.ContainingSymbol, known, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask177, name.GetLocation(), type.Name, provider, ProviderReplacement(type.Name)));
    }

    // What a call Rask.Data has a neutral form of is reported as, or null for any other call.
    private static (string Name, string Database, string Replacement)? Replacement(IInvocationOperation call, KnownTypes known)
    {
        var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
        if (IsFunctions(method, known))
        {
            return ("EF.Functions." + method.Name, ProviderOf(method.ContainingAssembly) ?? "one database's SQL", FunctionReplacement(method.Name));
        }

        if (known.MigrationBuilder is not null
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, known.MigrationBuilder)
            && string.Equals(method.Name, "Sql", StringComparison.Ordinal))
        {
            return ("Sql", "one database's SQL", "keep SQL in a migration, where it is not reported");
        }

        if (!IsEntityFrameworkCore(method.ContainingAssembly))
        {
            return null;
        }

        return method.Name switch
        {
            "FromSql" or "FromSqlRaw" or "FromSqlInterpolated" or "SqlQuery" or "SqlQueryRaw"
                => (method.Name, "one database's SQL", SqlReplacement(call, "a LINQ query over the model, which Rask translates for every database")),
            "ExecuteSql" or "ExecuteSqlAsync" or "ExecuteSqlRaw" or "ExecuteSqlRawAsync" or "ExecuteSqlInterpolated" or "ExecuteSqlInterpolatedAsync"
                => (method.Name, "one database's SQL", SqlReplacement(call, "a change through the aggregate, or ExecuteUpdate/ExecuteDelete over a LINQ query")),
            "ToSqlQuery" => (method.Name, "one database's SQL", "map the entity to a table or a view, and query it with LINQ"),
            "HasColumnType" => (method.Name, "one database's column types",
                "declare the value object's facets (Number(Value, Scale: 2), Text(Value, MaxLength: 200)) or HasPrecision / HasMaxLength; "
                + "for \"date\" on a DateTime, take a DateOnly or a Day value object (record X(DateOnly Value) : Day(Value)), date on SQL Server and PostgreSQL"),
            "SetContainerColumnType" => (method.Name, "one database's column types",
                "leave the JSON column's type to Rask, which keeps it the same at every compatibility level"),
            "UseCollation" => (method.Name, "one database's collations",
                "name the app's text order once with OrderTextBy(…) in ConfigureConventions, and sort with InTextOrder()"),
            "HasDefaultValueSql" when HasSql(call) => (method.Name, "one database's SQL",
                "b.Default(x => x.Property, value), b.StoredDefault(x => x.Property, stored), or b.Default(x => x.At, () => Clock.Now) (Clock.Wall for the database's wall clock)"),
            "HasComputedColumnSql" when HasSql(call) => (method.Name, "one database's SQL", "b.Computed(x => x.Property, x => …) in C#"),
            "HasFilter" when HasSql(call) => (method.Name, "one database's SQL", "HasIndex(…).Where(x => …) in C#"),
            _ => null,
        };
    }

    private static string FunctionReplacement(string name) => name switch
    {
        "Like" or "ILike" => "write Contains, StartsWith or EndsWith, which Rask matches in the app's text order on every database",
        "Collate" => "search case and accents aside with x.Loosely().Contains(typed), or sort with InTextOrder() in the order OrderTextBy(…) names once for the app",
        _ when name.StartsWith("DateDiff", StringComparison.Ordinal) => "subtract the moments (end - start), which Rask translates for every database",
        _ => "write the plain C# expression, which Rask translates for every database",
    };

    private static string ProviderReplacement(string name) => name switch
    {
        "IncludeProperties" => "declare a covering index with .Covering(x => …)",
        "UseCompatibilityLevel" => "Rask sets the SQL Server compatibility level itself",
        "SqlException" or "PostgresException" or "SqliteException"
            => "declare IsUnique(\"…\") on the index and the message on OnDelete(…), and catch the RaskValidationException every database raises",
        _ => Neutral,
    };

    // The SQL a call sends is named where Rask has the verb for it: an app lock or a row lock.
    private static string SqlReplacement(IInvocationOperation call, string otherwise)
    {
        foreach (var value in call.Arguments.Select(static a => a.Value))
        {
            var sql = value.ConstantValue is { HasValue: true, Value: string text } ? text : value.Syntax.ToString();
            if (sql.IndexOf("sp_getapplock", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "take Db.Lock(name).For(…), which holds on every database";
            }

            if (sql.IndexOf("UPDLOCK", StringComparison.OrdinalIgnoreCase) >= 0 || sql.IndexOf("HOLDLOCK", StringComparison.OrdinalIgnoreCase) >= 0
                || sql.IndexOf("FOR UPDATE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "read the rows with await X.Where(…).ForUpdate() inside Db.Transaction, which locks them on every database";
            }
        }

        return otherwise;
    }

    // HasDefaultValueSql() and HasFilter(null) say "none": only a call that hands SQL over is reported.
    private static bool HasSql(IInvocationOperation call) =>
        call.Arguments.Any(static a => a.Parameter is { Type.SpecialType: SpecialType.System_String }
                                       && a.ArgumentKind != ArgumentKind.DefaultValue
                                       && a.Value.ConstantValue is not { HasValue: true, Value: null });

    private static bool IsExempt(SyntaxNode node, SemanticModel? model, ISymbol? where, KnownTypes known, System.Threading.CancellationToken cancellation)
    {
        if (node.SyntaxTree.FilePath.Replace('\\', '/').Split('/')
            .Any(static segment => string.Equals(segment, "Migrations", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        for (var type = where as INamedTypeSymbol ?? where?.ContainingType; type is not null; type = type.ContainingType)
        {
            if (DerivesFrom(type, known.Migration) || DerivesFrom(type, known.ModelSnapshot))
            {
                return true;
            }
        }

        return model is not null && node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
            .Any(call => model.GetOperation(call, cancellation) is IInvocationOperation selection && IsProviderSelection(selection, known));
    }

    private static bool IsProviderSelection(IInvocationOperation call, KnownTypes known)
    {
        var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
        return ProviderSelection.Contains(method.Name)
               && method is { IsExtensionMethod: true, Parameters.Length: > 0 }
               && DerivesFrom(method.Parameters[0].Type as INamedTypeSymbol, known.OptionsBuilder);
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol? baseType)
    {
        for (var current = type; current is not null && baseType is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFunctions(IMethodSymbol method, KnownTypes known) =>
        method is { IsExtensionMethod: true, Parameters.Length: > 0 }
        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, known.Functions);

    private static bool IsEntityFrameworkCore(IAssemblySymbol? assembly) =>
        assembly?.Name is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Relational";

    // The database an assembly is written for, or null for one that is not a provider's or its driver's.
    private static string? ProviderOf(IAssemblySymbol? assembly) => assembly?.Name switch
    {
        null => null,
        "Microsoft.Data.SqlClient" => "SQL Server",
        "Npgsql" => "PostgreSQL",
        "Microsoft.Data.Sqlite" => "SQLite",
        var name when name.StartsWith("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal) => "SQL Server",
        var name when name.StartsWith("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal) => "PostgreSQL",
        var name when name.StartsWith("Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal) => "SQLite",
        _ => null,
    };

    // The name a member is called by — the span a diagnostic points at, so a long chain is not underlined whole.
    private static SyntaxNode NameOf(SyntaxNode syntax) => syntax switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Name,
        InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } => binding.Name,
        MemberAccessExpressionSyntax access => access.Name,
        _ => syntax,
    };

    private sealed record KnownTypes(
        INamedTypeSymbol Functions,
        INamedTypeSymbol? OptionsBuilder,
        INamedTypeSymbol? Migration,
        INamedTypeSymbol? ModelSnapshot,
        INamedTypeSymbol? MigrationBuilder);
}
