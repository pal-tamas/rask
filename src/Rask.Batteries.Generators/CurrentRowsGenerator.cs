using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Finds the aggregate that says <c>Rask.Data.ITenant</c> and the one that says <c>Rask.Data.IUser</c>, and emits
/// <c>Current.Tenant</c> / <c>Current.RequiredTenant</c> and <c>Current.User</c> / <c>Current.RequiredUser</c>,
/// typed as each one's read face.
/// </summary>
/// <remarks>
/// <para>
/// The accessors read through <c>Rask.Data.CurrentRows</c>, and a <c>[ModuleInitializer]</c> names the aggregate
/// there with the two lambdas that find its read face by key — closed over the types at compile time, never
/// reflected, so a trimmed publish keeps them.
/// </para>
/// <para>
/// The tenant's key must be the one its <c>PerTenant</c> tables keep in <c>TenantId</c>: a <c>Guid</c> (or a
/// <c>string</c> holding one) for a <c>Guid</c> column, the same number type for a number. A project with
/// <c>PerTenant</c> tables and no <c>ITenant</c> is told how to say it.
/// </para>
/// </remarks>
[Generator]
public sealed class CurrentRowsGenerator : IIncrementalGenerator
{
    private const string RaskData = "Rask.Data";
    private const string TenantMarker = "ITenant";
    private const string UserMarker = "IUser";
    private const string GuidType = "global::System.Guid";

    private static readonly DiagnosticDescriptor Rask113 = new(
        "RASK113",
        "More than one aggregate says ITenant or IUser",
        "'{0}' and '{1}' both say {2}, so Rask cannot tell which one is this app's {3}, and Current.{4} is not "
        + "generated. Say {2} on one of them.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Current.Tenant and Current.User are found from the aggregate that says ITenant or IUser, which "
                     + "needs there to be one. With two, either answer would be a guess at which row the work is for.",
        helpLinkUri: DiagnosticHelp.Link("RASK113"));

    private static readonly DiagnosticDescriptor Rask114 = new(
        "RASK114",
        "The tenant's or the user's key cannot be the current one",
        "'{0}' says {1}, but {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "The framework carries the current tenant and user as a Guid, and reads the row back by the key "
                     + "that Guid stands for: a Guid, a string holding one, or the int or long it carries. The tenant's "
                     + "key must also be the one its PerTenant tables keep, or a filter and the row would name "
                     + "different tenants.",
        helpLinkUri: DiagnosticHelp.Link("RASK114"));

    private static readonly DiagnosticDescriptor Rask115 = new(
        "RASK115",
        "Tenant-scoped tables and no tenant aggregate",
        "'{0}' is PerTenant, but no aggregate in this project says ITenant, so there is no Current.Tenant. Say it on "
        + "the tenant aggregate: public sealed class Tenant : Aggregate<{1}>, ITenant.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Current.Tenant is the app's own tenant row, generated once one aggregate says ITenant. It used to "
                     + "be the tenant's Guid; the framework's own readers keep that, and an app reads the row.",
        helpLinkUri: DiagnosticHelp.Link("RASK115"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var found = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, token) => ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, token) is INamedTypeSymbol symbol
                    ? Describe(symbol)
                    : null)
            .Where(static f => f is not null)
            .Select(static (f, _) => f!);

        context.RegisterSourceOutput(found.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static Found? Describe(INamedTypeSymbol symbol)
    {
        if (symbol is { IsAbstract: true } or { IsStatic: true } or { IsGenericType: true })
        {
            return null;
        }

        var marker = MarkerOf(symbol);
        var tenantColumn = marker is null && AggregateShape.IsAggregate(symbol) && AggregateShape.Has(symbol, AggregateShape.PerTenant)
            ? TenantColumnOf(symbol)
            : null;

        if (marker is null && tenantColumn is null)
        {
            return null;
        }

        AggregateShape.TryGetIdType(symbol, out var idType);

        return new Found(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.Name,
            marker,
            KeyOf(symbol, idType),
            idType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "no key",
            ReadFaceName.Fqn(symbol),
            symbol.DeclaredAccessibility == Accessibility.Public,
            tenantColumn,
            SymbolLocation.From(symbol));
    }

    // ITenant or IUser, by name and namespace through every interface the class has, so a user deriving from Rask.Auth's
    // Authenticatable says IUser without writing it.
    private static string? MarkerOf(INamedTypeSymbol symbol) =>
        symbol.AllInterfaces
            .Where(static face => face.Name is TenantMarker or UserMarker && face.ContainingNamespace?.ToDisplayString() is RaskData)
            .Select(static face => face.Name)
            .FirstOrDefault();

    // The key, when it is one the current row can be read back by: a single Guid, string, int or long on an aggregate.
    private static string? KeyOf(INamedTypeSymbol symbol, ITypeSymbol? idType)
    {
        if (idType is null || !AggregateShape.IsAggregate(symbol) || CompositeKeyShape.HasCompositeKey(symbol))
        {
            return null;
        }

        return idType.SpecialType switch
        {
            SpecialType.System_String => "string",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Int64 => "long",
            _ => idType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is GuidType ? GuidType : null,
        };
    }

    // The type a PerTenant root keeps its tenant in: the TenantId it declares, else PerTenant<TKey>'s, else Guid —
    // as the runtime's TenantColumn decides it. Written as the key type that agrees with it.
    private static string TenantColumnOf(INamedTypeSymbol symbol)
    {
        for (var type = symbol; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(AggregateShape.TenantColumn).OfType<IPropertySymbol>().FirstOrDefault() is { } declared)
            {
                var column = declared.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                    ? nullable.TypeArguments[0]
                    : declared.Type;
                return ColumnName(column.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return ColumnName(AggregateShape.TenantKeyOf(symbol) ?? GuidType);
    }

    private static string ColumnName(string fullyQualified) => fullyQualified switch
    {
        "int" or "global::System.Int32" => "int",
        "long" or "global::System.Int64" => "long",
        _ => GuidType,
    };

    private static void Emit(SourceProductionContext context, ImmutableArray<Found> all)
    {
        if (all.IsDefaultOrEmpty)
        {
            return;
        }

        var distinct = all.Distinct().OrderBy(static f => f.FullyQualifiedName, StringComparer.Ordinal).ToList();
        var tables = distinct.Where(static f => f.TenantColumn is not null).ToList();

        var tenant = One(context, distinct, TenantMarker, "tenant", "Tenant");
        if (tenant is null)
        {
            if (tables.Count > 0 && distinct.All(static f => f.Marker is not TenantMarker))
            {
                var first = tables[0];
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask115, first.Location?.ToLocation(), first.Name, Keyword(first.TenantColumn!)));
            }
        }
        else if (Usable(context, tenant, tables))
        {
            context.AddSource("__RaskCurrentTenant.g.cs", SourceText.From(Source(tenant, "Tenant", "UseTenant"), Encoding.UTF8));
        }

        if (One(context, distinct, UserMarker, "user", "User") is { } user && Usable(context, user, []))
        {
            context.AddSource("__RaskCurrentUser.g.cs", SourceText.From(Source(user, "User", "UseUser"), Encoding.UTF8));
        }
    }

    // The one class that says the marker, or null — with RASK113 when more than one does.
    private static Found? One(SourceProductionContext context, System.Collections.Generic.List<Found> all, string marker, string what, string member)
    {
        var saying = all.Where(f => string.Equals(f.Marker, marker, StringComparison.Ordinal)).ToList();
        if (saying.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask113, saying[1].Location?.ToLocation(), saying[0].FullyQualifiedName, saying[1].FullyQualifiedName, marker, what, member));
            return null;
        }

        return saying.Count == 1 ? saying[0] : null;
    }

    // RASK114 for a key the row cannot be read back by, or a tenant key its tables disagree with.
    private static bool Usable(SourceProductionContext context, Found marked, System.Collections.Generic.List<Found> tables)
    {
        if (marked.Key is not { } key)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask114, marked.Location?.ToLocation(), marked.Name, marked.Marker,
                $"it is keyed by {marked.KeyName}; the current {(marked.Marker is TenantMarker ? "tenant" : "user")} is read back "
                + "by a single key of Guid, string (holding a Guid), int or long, on an aggregate"));
            return false;
        }

        var agreeing = key is "int" or "long" ? key : GuidType;
        if (tables.FirstOrDefault(t => !string.Equals(t.TenantColumn, agreeing, StringComparison.Ordinal)) is { } other)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask114, marked.Location?.ToLocation(), marked.Name, marked.Marker,
                $"its key is {marked.KeyName} and '{other.Name}' keeps its tenant as {Keyword(other.TenantColumn!)}; give the "
                + "tenant the key its tables keep, or the tables the tenant's"));
            return false;
        }

        return true;
    }

    private static string Keyword(string type) => type is GuidType ? "Guid" : type;

    private static string Source(Found marked, string member, string use)
    {
        var read = marked.ReadFace;
        var access = marked.IsPublic ? "public" : "internal";
        var what = member.ToLowerInvariant();
        var source = new StringBuilder();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Rask.Data;");
        source.AppendLine();
        source.Append("/// <summary>The app's ").Append(what).Append(" row, as <c>Current.").Append(member)
            .Append("</c>: <see cref=\"").Append(marked.FullyQualifiedName.Replace("global::", string.Empty))
            .AppendLine("\" /> says it.</summary>");
        source.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.Append(access).Append(" static class Current").Append(member).AppendLine("Row");
        source.AppendLine("{");
        source.AppendLine("    extension(global::Rask.Current)");
        source.AppendLine("    {");
        source.Append("        /// <summary>The ").Append(what).Append(" the work in flight is for, as its own row, or <see langword=\"null\" /> when it is for no ")
            .Append(what).AppendLine(".</summary>");
        source.AppendLine("        /// <remarks>Read from a cache filled as the work starts, so it is not a query. Read-only: to change it, load the aggregate by <c>Id</c> and save that.</remarks>");
        source.Append("        public static ").Append(read).Append("? ").Append(member)
            .Append(" => global::Rask.Data.CurrentRows.").Append(member).Append('<').Append(read).AppendLine(">();");
        source.AppendLine();
        source.Append("        /// <summary>The ").Append(what).Append(" the work in flight is for, as its own row, or a thrown exception when it is for no ")
            .Append(what).AppendLine(".</summary>");
        source.Append("        public static ").Append(read).Append(" Required").Append(member)
            .Append(" => global::Rask.Data.CurrentRows.Required").Append(member).Append('<').Append(read).AppendLine(">();");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.Append("    internal static void Register() => global::Rask.Data.CurrentRows.").Append(use).Append('<')
            .Append(marked.FullyQualifiedName).Append(", ").Append(read).Append(", ").Append(marked.Key).AppendLine(">(");
        source.AppendLine("        static key => row => row.Id == key,");
        source.AppendLine("        static row => row.Id);");
        source.AppendLine("}");

        return source.ToString();
    }

    private sealed record Found(
        string FullyQualifiedName,
        string Name,
        string? Marker,
        string? Key,
        string KeyName,
        string ReadFace,
        bool IsPublic,
        string? TenantColumn,
        SymbolLocation? Location);
}
