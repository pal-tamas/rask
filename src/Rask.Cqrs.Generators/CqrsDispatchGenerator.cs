using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Cqrs.Generators;

/// <summary>
/// Emits a reflection-free dispatch table for Rask.Cqrs. For every handler
/// (<c>IQueryHandler</c>/<c>ICommandHandler</c>/<c>INotificationHandler</c>) it generates a
/// closed-generic invoker and a per-assembly <c>[ModuleInitializer]</c> that registers the invoker
/// and the handler's DI descriptor into <c>CqrsRegistry</c>. No runtime reflection or assembly
/// scanning — the trimmer keeps handler constructors alive via <c>[DynamicDependency]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CqrsDispatchGenerator : IIncrementalGenerator
{
    private const string Namespace = "Rask.Cqrs";

    private static readonly DiagnosticDescriptor Rask028 = new(
        "RASK028",
        "Ambiguous request handler",
        "Request type '{0}' is handled by more than one handler; a query or command must have exactly one handler",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK028"));

    private static readonly DiagnosticDescriptor Rask029 = new(
        "RASK029",
        "Handler cannot be registered",
        "Handler '{0}' {1}; it is skipped, so dispatching its request will throw at runtime",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK029"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Match any type declaration with a base list (class OR record class — a record handler is a
        // RecordDeclarationSyntax, not a ClassDeclarationSyntax); the semantic pass filters to
        // non-abstract classes, so interfaces/structs/record structs fall out there.
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax c && c.BaseList is { Types.Count: > 0 } &&
                                    !c.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(candidates.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not TypeDeclarationSyntax typeDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // Handlers must be concrete classes (record classes included). Interfaces, structs and
        // record structs can't be DI-constructed handlers.
        if (symbol.IsAbstract || symbol.TypeKind != TypeKind.Class)
        {
            return null;
        }

        var handlers = new List<HandlerModel>();
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.ContainingNamespace?.ToDisplayString() != Namespace || !iface.IsGenericType)
            {
                continue;
            }

            var kind = iface.MetadataName switch
            {
                "IQueryHandler`2" => (HandlerKind?)HandlerKind.Query,
                "ICommandHandler`1" => HandlerKind.CommandVoid,
                "ICommandHandler`2" => HandlerKind.CommandResult,
                "INotificationHandler`1" => HandlerKind.Notification,
                _ => null,
            };

            if (kind is null)
            {
                continue;
            }

            var args = iface.TypeArguments;
            var requestFqn = Fqn(args[0]);
            var resultFqn = kind switch
            {
                HandlerKind.Query => Fqn(args[1]),
                HandlerKind.CommandResult => Fqn(args[1]),
                HandlerKind.CommandVoid => "global::Rask.Cqrs.Unit",
                _ => string.Empty,
            };

            handlers.Add(new HandlerModel(
                kind.Value,
                Fqn(symbol),
                requestFqn,
                resultFqn,
                Fqn(iface),
                DescribeRegisterability(symbol)));
        }

        if (handlers.Count == 0)
        {
            return null;
        }

        return new Candidate(new EquatableArray<HandlerModel>(handlers), LocationInfo.From(symbol));
    }

    // Returns the reason a handler cannot be registered, or null when it is fine. Open generic
    // handlers can't be closed at registration time; a handler with no public constructor can't be
    // built by the DI container.
    private static string? DescribeRegisterability(INamedTypeSymbol symbol)
    {
        if (symbol.IsGenericType)
        {
            return "is an open generic type";
        }

        if (!symbol.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public))
        {
            return "has no public constructor";
        }

        return null;
    }

    private static void Emit(SourceProductionContext spc, IEnumerable<Candidate> candidates)
    {
        // Dedup by (handler type, service interface): a handler split across partial declarations that
        // each carry a base list is visited once per partial and would otherwise be counted twice —
        // which would (a) misfire RASK028 (ambiguous) against a single legitimate handler and
        // (b) emit duplicate registrations. A genuine second handler has a different type FQN.
        var models = new List<(HandlerModel Model, LocationInfo? Location)>();
        var seen = new HashSet<(string, string)>();
        foreach (var candidate in candidates)
        {
            foreach (var handler in candidate.Handlers)
            {
                if (seen.Add((handler.HandlerTypeFqn, handler.ServiceInterfaceFqn)))
                {
                    models.Add((handler, candidate.Location));
                }
            }
        }

        if (models.Count == 0)
        {
            return;
        }

        // RASK029: skip unregisterable handlers with a warning.
        var registerable = new List<(HandlerModel Model, LocationInfo? Location)>();
        foreach (var entry in models)
        {
            if (entry.Model.RegisterabilityProblem is { } problem)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask029, entry.Location?.ToLocation(), entry.Model.HandlerTypeFqn, problem));
                continue;
            }

            registerable.Add(entry);
        }

        var requests = registerable
            .Where(e => e.Model.Kind != HandlerKind.Notification)
            .ToList();
        var notifications = registerable
            .Where(e => e.Model.Kind == HandlerKind.Notification)
            .ToList();

        // RASK028: a query/command must have exactly one handler.
        var requestGroups = requests
            .GroupBy(e => e.Model.RequestTypeFqn, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var uniqueRequests = new List<HandlerModel>();
        foreach (var group in requestGroups)
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                foreach (var member in members)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Rask028, member.Location?.ToLocation(), member.Model.RequestTypeFqn));
                }
            }

            uniqueRequests.Add(members[0].Model);
        }

        var notificationTypes = notifications
            .Select(e => e.Model.RequestTypeFqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var handlerImpls = registerable
            .Select(e => (e.Model.ServiceInterfaceFqn, e.Model.HandlerTypeFqn, IsNotification: e.Model.Kind == HandlerKind.Notification))
            .Distinct()
            .OrderBy(t => t.ServiceInterfaceFqn, StringComparer.Ordinal)
            .ThenBy(t => t.HandlerTypeFqn, StringComparer.Ordinal)
            .ToList();

        var distinctImplTypes = registerable
            .Select(e => e.Model.HandlerTypeFqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        spc.AddSource("__RaskCqrsRegistry.g.cs", SourceText.From(
            Build(uniqueRequests, notifications, notificationTypes, handlerImpls, distinctImplTypes),
            Encoding.UTF8));
    }

    private static string Build(
        List<HandlerModel> requests,
        List<(HandlerModel Model, LocationInfo? Location)> notifications,
        List<string> notificationTypes,
        List<(string ServiceInterfaceFqn, string HandlerTypeFqn, bool IsNotification)> handlerImpls,
        List<string> distinctImplTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0618");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskCqrsRegistry");
        sb.AppendLine("{");

        // Keep handler constructors under the trimmer (resolved via DI GetRequiredService/GetServices).
        foreach (var impl in distinctImplTypes)
        {
            sb.Append("    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(")
                .Append("global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors")
                .Append(", typeof(").Append(impl).AppendLine("))]");
        }

        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init()");
        sb.AppendLine("    {");

        for (var i = 0; i < requests.Count; i++)
        {
            sb.Append("        global::Rask.Cqrs.CqrsRegistry.RegisterRequest(typeof(")
                .Append(requests[i].RequestTypeFqn).Append("), __Request_").Append(i).AppendLine(");");
        }

        for (var i = 0; i < notificationTypes.Count; i++)
        {
            sb.Append("        global::Rask.Cqrs.CqrsRegistry.RegisterNotification(typeof(")
                .Append(notificationTypes[i]).Append("), __Notify_").Append(i).AppendLine(");");
        }

        foreach (var impl in handlerImpls)
        {
            var method = impl.IsNotification ? "TryAddEnumerable" : "TryAdd";
            sb.Append("        global::Rask.Cqrs.CqrsRegistry.RegisterServices(static (services, lifetime) => ")
                .Append("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.")
                .Append(method).AppendLine("(services,")
                .Append("            new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(")
                .Append(impl.ServiceInterfaceFqn).Append("), typeof(").Append(impl.HandlerTypeFqn)
                .AppendLine("), lifetime)));");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        for (var i = 0; i < requests.Count; i++)
        {
            EmitRequestInvoker(sb, requests[i], i);
        }

        for (var i = 0; i < notificationTypes.Count; i++)
        {
            EmitNotificationInvoker(sb, notificationTypes[i], i);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitRequestInvoker(StringBuilder sb, HandlerModel model, int index)
    {
        var request = model.RequestTypeFqn;
        var result = model.ResultTypeFqn;
        var service = model.ServiceInterfaceFqn;
        var behavior = $"global::Rask.Cqrs.IPipelineBehavior<{request}, {result}>";

        sb.Append("    private static global::System.Threading.Tasks.Task __Request_").Append(index)
            .AppendLine("(global::System.IServiceProvider provider, object request, global::System.Threading.CancellationToken ct)");
        sb.AppendLine("    {");
        sb.Append("        var typed = (").Append(request).AppendLine(")request;");
        sb.Append("        var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<")
            .Append(service).AppendLine(">(provider);");
        sb.Append("        var behaviors = global::System.Linq.Enumerable.ToArray(")
            .Append("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<")
            .Append(behavior).AppendLine(">(provider));");

        if (model.Kind == HandlerKind.CommandVoid)
        {
            sb.Append("        global::Rask.Cqrs.RequestHandlerDelegate<").Append(result)
                .AppendLine("> next = async () => { await handler.HandleAsync(typed, ct).ConfigureAwait(false); return default; };");
        }
        else
        {
            sb.Append("        global::Rask.Cqrs.RequestHandlerDelegate<").Append(result)
                .AppendLine("> next = () => handler.HandleAsync(typed, ct);");
        }

        sb.AppendLine("        for (int i = behaviors.Length - 1; i >= 0; i--)");
        sb.AppendLine("        {");
        sb.AppendLine("            var behavior = behaviors[i];");
        sb.AppendLine("            var prev = next;");
        sb.AppendLine("            next = () => behavior.HandleAsync(typed, prev, ct);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return next();");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitNotificationInvoker(StringBuilder sb, string notificationType, int index)
    {
        sb.Append("    private static global::System.Threading.Tasks.Task __Notify_").Append(index)
            .AppendLine("(global::System.IServiceProvider provider, object notification, global::System.Threading.CancellationToken ct)");
        sb.AppendLine("    {");
        sb.Append("        var typed = (").Append(notificationType).AppendLine(")notification;");
        sb.Append("        var handlers = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<")
            .Append("global::Rask.Cqrs.INotificationHandler<").Append(notificationType).AppendLine(">>(provider);");
        sb.Append("        return global::Rask.Cqrs.NotificationDispatch.PublishAll<").Append(notificationType)
            .AppendLine(">(provider, typed, handlers, ct);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    // FullyQualifiedFormat drops nullable reference annotations, so a handler for IQuery<string?>
    // would be registered as IQueryHandler<TQuery, string> — whose `where TQuery : IQuery<TResult>`
    // constraint then mismatches the query's IQuery<string?> and warns CS8631 at the typeof(...) site.
    // Preserve the `?` so the emitted service type matches the declared handler exactly.
    private static readonly SymbolDisplayFormat FqnFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string Fqn(ITypeSymbol symbol) =>
        symbol.ToDisplayString(FqnFormat);

    private enum HandlerKind
    {
        Query,
        CommandVoid,
        CommandResult,
        Notification,
    }

    private sealed record HandlerModel(
        HandlerKind Kind,
        string HandlerTypeFqn,
        string RequestTypeFqn,
        string ResultTypeFqn,
        string ServiceInterfaceFqn,
        string? RegisterabilityProblem) : IEquatable<HandlerModel>;

    private sealed record Candidate(EquatableArray<HandlerModel> Handlers, LocationInfo? Location);

    private sealed record LocationInfo(
        string FilePath, int Start, int Length, int StartLine, int StartChar, int EndLine, int EndChar)
    {
        public Location ToLocation() => Location.Create(
            FilePath,
            new TextSpan(Start, Length),
            new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                new Microsoft.CodeAnalysis.Text.LinePosition(StartLine, StartChar),
                new Microsoft.CodeAnalysis.Text.LinePosition(EndLine, EndChar)));

        public static LocationInfo? From(ISymbol symbol)
        {
            var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (loc?.SourceTree is null)
            {
                return null;
            }

            var span = loc.GetLineSpan();
            return new LocationInfo(
                loc.SourceTree.FilePath,
                loc.SourceSpan.Start,
                loc.SourceSpan.Length,
                span.StartLinePosition.Line,
                span.StartLinePosition.Character,
                span.EndLinePosition.Line,
                span.EndLinePosition.Character);
        }
    }
}
