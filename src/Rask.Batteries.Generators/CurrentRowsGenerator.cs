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
/// typed as each one's read face — and, from the same key, <c>Current.TenantId</c> / <c>Current.RequiredTenantId</c>
/// for every tenant key and <c>Current.UserId</c> / <c>Current.RequiredUserId</c> / <c>Current.UseUser</c> for an int,
/// long or typed-id user key — and <c>Tenant.Use(tenantId)</c> for a tenant keyed by a typed id.
/// </summary>
/// <remarks>
/// <para>
/// The accessors read through <c>Rask.Data.CurrentRows</c>, and a <c>[ModuleInitializer]</c> names the aggregate
/// there with the two lambdas that find its read face by key — closed over the types at compile time, never
/// reflected, so a trimmed publish keeps them.
/// </para>
/// <para>
/// The tenant's key must be the one its <c>PerTenant</c> tables keep in <c>TenantId</c>: a <c>Guid</c> (or a
/// <c>string</c> holding one) for a <c>Guid</c> column, the same number type for a number, the same typed id for a
/// typed-id column (<c>TenantId? TenantId</c>, or <c>PerTenant&lt;TenantId&gt;</c>). A project with
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
                     + "that Guid stands for: a Guid, a string holding one, the int or long it carries, or a typed id "
                     + "holding a Guid, an int or a long. The tenant's key must also be the one its PerTenant tables "
                     + "keep, or a filter and the row would name different tenants.",
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
        var (key, keyValue) = KeyOf(symbol, idType);

        return new Found(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.Name,
            marker,
            key,
            keyValue,
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

    // The key, when it is one the current row can be read back by: a single Guid, string, int or long on an aggregate,
    // or a typed id holding a Guid, an int or a long — then with the value it holds, which is what the framework carries.
    private static (string? Key, string? Value) KeyOf(INamedTypeSymbol symbol, ITypeSymbol? idType)
    {
        if (idType is null || !AggregateShape.IsAggregate(symbol) || CompositeKeyShape.HasCompositeKey(symbol))
        {
            return (null, null);
        }

        var fullyQualified = idType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return idType.SpecialType switch
        {
            SpecialType.System_String => ("string", null),
            SpecialType.System_Int32 => ("int", null),
            SpecialType.System_Int64 => ("long", null),
            _ when fullyQualified is GuidType => (GuidType, null),
            _ => TypedValueOf(idType) is { } value ? (fullyQualified, value) : (null, null),
        };
    }

    // What a typed id over a Guid, an int or a long holds, as the type the generated code names; null for any other.
    private static string? TypedValueOf(ITypeSymbol type) => TypedIdShape.Of(type) switch
    {
        TypedIdValue.Guid => GuidType,
        TypedIdValue.Int32 => "int",
        TypedIdValue.Int64 => "long",
        _ => null,
    };

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
                return ColumnName(column);
            }
        }

        return AggregateShape.TenantKeyTypeOf(symbol) is { } key ? ColumnName(key) : GuidType;
    }

    // A typed id keeps its own name, so only a tenant keyed by that same id agrees with it; anything else is the key
    // type that agrees with it.
    private static string ColumnName(ITypeSymbol column)
    {
        var fullyQualified = column.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (TypedValueOf(column) is not null)
        {
            return fullyQualified;
        }

        return fullyQualified switch
        {
            "int" or "global::System.Int32" => "int",
            "long" or "global::System.Int64" => "long",
            _ => GuidType,
        };
    }

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
            context.AddSource("__RaskCurrentTenantId.g.cs", SourceText.From(TenantIdSource(tenant), Encoding.UTF8));
        }

        if (One(context, distinct, UserMarker, "user", "User") is { } user && Usable(context, user, []))
        {
            context.AddSource("__RaskCurrentUser.g.cs", SourceText.From(Source(user, "User", "UseUser"), Encoding.UTF8));
            if (user.Key is "int" or "long" || user.KeyValue is not null)
            {
                context.AddSource("__RaskCurrentUserId.g.cs", SourceText.From(UserIdSource(user), Encoding.UTF8));
            }
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
                + "by a single key of Guid, string (holding a Guid), int, long, or a typed id over Guid, int or long, "
                + "on an aggregate"));
            return false;
        }

        var agreeing = marked.KeyValue is not null || key is "int" or "long" ? key : GuidType;
        if (tables.FirstOrDefault(t => !string.Equals(t.TenantColumn, agreeing, StringComparison.Ordinal)) is { } other)
        {
            var fix = marked.KeyValue is not null
                ? $"give the tables the tenant's key: {marked.KeyName}? TenantId, or PerTenant<{marked.KeyName}>"
                : "give the tenant the key its tables keep, or the tables the tenant's";
            context.ReportDiagnostic(Diagnostic.Create(
                Rask114, marked.Location?.ToLocation(), marked.Name, marked.Marker,
                $"its key is {marked.KeyName} and '{other.Name}' keeps its tenant as {Keyword(other.TenantColumn!)}; {fix}"));
            return false;
        }

        return true;
    }

    // As the sentence names a type: Guid, a number keyword, or a typed id's own name.
    private static string Keyword(string type) =>
        type is GuidType ? "Guid" : type.Substring(type.LastIndexOf('.') + 1);

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
            .Append(marked.FullyQualifiedName).Append(", ").Append(read).Append(", ").Append(marked.Key);
        if (marked.KeyValue is { } value)
        {
            // A typed id: the framework carries the value it holds, and the last two lambdas cross between them.
            source.Append(", ").Append(value).AppendLine(">(");
            source.AppendLine("        static key => row => row.Id == key,");
            source.AppendLine("        static row => row.Id,");
            source.Append("        static value => new ").Append(marked.Key).AppendLine("(value),");
            source.AppendLine("        static key => key.Value);");
        }
        else
        {
            source.AppendLine(">(");
            source.AppendLine("        static key => row => row.Id == key,");
            source.AppendLine("        static row => row.Id);");
        }

        source.AppendLine("}");

        return source.ToString();
    }

    // Current.UserId / RequiredUserId / UseUser as the user's own number. In the GLOBAL namespace on purpose: C# looks
    // for an extension in the closest scope first, and the global namespace's own types are searched before the
    // namespaces a file's usings import — so this beats Rask.Data's Guid? Current.UserId wherever `using Rask.Data;`
    // sits at the top of the file, and a Guid passed to UseUser still finds Rask.Data's overload.
    private static string UserIdSource(Found user)
    {
        var key = user.Key!;
        var carried = user.KeyValue ?? key;
        var source = Header(user, "RaskCurrentUserId", "Current.UserId");
        source.AppendLine("    extension(global::Rask.Current)");
        source.AppendLine("    {");
        source.AppendLine("        /// <summary>The id of the user the work in flight is for, or <see langword=\"null\" /> when it is for nobody.</summary>");
        source.AppendLine("        /// <remarks>An explicit <c>Current.UseUser</c> first — a background job runs for the user its row recorded — then the signed-in principal's <c>NameIdentifier</c> claim, which holds the number written out.</remarks>");
        source.Append("        public static ").Append(key).Append("? UserId => global::Rask.Data.CurrentRows.UserId<").Append(carried).Append(">()")
            .Append(Wrapped(user)).AppendLine(";");
        source.AppendLine();
        source.AppendLine("        /// <summary>The id of the user the work in flight is for, or a thrown exception when there is none.</summary>");
        source.Append("        public static ").Append(key).Append(" RequiredUserId => ")
            .Append(RequiredOf(user, "global::Rask.Data.CurrentRows.RequiredUserId<" + carried + ">()")).AppendLine(";");
        source.AppendLine();
        source.AppendLine("        /// <summary>Makes <paramref name=\"userId\" /> the current user until the returned scope is disposed.</summary>");
        source.AppendLine("        /// <param name=\"userId\">The user to work for, or <see langword=\"null\" /> to work for nobody.</param>");
        source.AppendLine("        /// <returns>A scope that restores the previous user.</returns>");
        source.Append("        public static global::System.IDisposable UseUser(").Append(key).Append("? userId) => global::Rask.Data.CurrentRows.WorkFor<")
            .Append(carried).Append(">(userId").Append(user.KeyValue is null ? string.Empty : "?.Value").AppendLine(");");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    // Current.TenantId / RequiredTenantId as the tenant aggregate's key — a number, a Guid, or a string holding one. In
    // the global namespace like the user's, so it reads the same wherever Current does.
    private static string TenantIdSource(Found tenant)
    {
        var key = tenant.Key!;
        var carried = tenant.KeyValue ?? (key is "string" ? GuidType : key);
        var text = key is "string" ? "?.ToString(\"D\")" : Wrapped(tenant);
        var required = "global::Rask.Data.CurrentRows.RequiredTenantId<" + carried + ">()" + (key is "string" ? ".ToString(\"D\")" : string.Empty);
        var source = Header(tenant, "RaskCurrentTenantId", "Current.TenantId");
        source.AppendLine("    extension(global::Rask.Current)");
        source.AppendLine("    {");
        source.AppendLine("        /// <summary>The id of the tenant the work in flight belongs to, or <see langword=\"null\" /> when it belongs to none — or to every one, inside <c>Tenant.Across()</c>.</summary>");
        source.Append("        public static ").Append(key).Append("? TenantId => global::Rask.Data.CurrentRows.TenantId<").Append(carried).Append(">()").Append(text).AppendLine(";");
        source.AppendLine();
        source.AppendLine("        /// <summary>The id of the tenant the work in flight belongs to, or a thrown exception when it belongs to none, or <c>Tenant.Across()</c> is open.</summary>");
        source.Append("        public static ").Append(key).Append(" RequiredTenantId => ").Append(RequiredOf(tenant, required)).AppendLine(";");
        source.AppendLine("    }");

        if (tenant.KeyValue is not null)
        {
            source.AppendLine();
            source.AppendLine("    extension(global::Rask.Data.Tenant)");
            source.AppendLine("    {");
            source.AppendLine("        /// <summary>Makes <paramref name=\"tenant\" /> the tenant until the returned scope is disposed.</summary>");
            source.AppendLine("        /// <param name=\"tenant\">The tenant to work in.</param>");
            source.AppendLine("        /// <returns>A scope that restores the previous tenant.</returns>");
            source.Append("        public static global::System.IDisposable Use(").Append(key)
                .AppendLine(" tenant) => global::Rask.Data.Tenant.Use(tenant.Value);");
            source.AppendLine("    }");
        }

        source.AppendLine("}");
        return source.ToString();
    }

    // A typed id read back from the value the framework carries: ` is { } v ? new TenantId(v) : null`, else nothing.
    private static string Wrapped(Found marked) =>
        marked.KeyValue is null ? string.Empty : " is { } v ? new " + marked.Key + "(v) : null";

    // A typed id's required read, wrapped: `new(read)`.
    private static string RequiredOf(Found marked, string read) =>
        marked.KeyValue is null ? read : "new(" + read + ")";

    private static StringBuilder Header(Found marked, string className, string what)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.Append("/// <summary><c>").Append(what).Append("</c>, as the key <see cref=\"").Append(marked.FullyQualifiedName.Replace("global::", string.Empty))
            .AppendLine("\" /> is keyed by.</summary>");
        source.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.Append(marked.IsPublic ? "public" : "internal").Append(" static class ").AppendLine(className);
        source.AppendLine("{");
        return source;
    }

    private sealed record Found(
        string FullyQualifiedName,
        string Name,
        string? Marker,
        string? Key,
        string? KeyValue,
        string KeyName,
        string ReadFace,
        bool IsPublic,
        string? TenantColumn,
        SymbolLocation? Location);
}
