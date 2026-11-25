using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
///     Makes a durable handler of every <c>static Task On(E e)</c> an aggregate declares: the outbox stores a row for it in
///     the transaction that raised <c>E</c>, and runs it from there after the commit — in its own transaction, retried
///     until it succeeds — exactly as it runs an <c>IDurableHandler&lt;E&gt;</c> class. No class to write, no DI.
/// </summary>
/// <remarks>
///     <para>
///         The handler is stored as <c>{Aggregate}:{Event}</c> (full names), so its rows stay its own across a rebuild;
///         renaming the aggregate or the event renames it, like renaming a handler class does.
///     </para>
///     <para>
///         The registration is a module initializer inside the aggregate's own partial class, which is what lets it call
///         a private <c>On</c>. RASK179 refuses an <c>On</c> of the wrong shape; RASK191 an aggregate the registration
///         cannot be added to.
///     </para>
/// </remarks>
[Generator]
public sealed class AggregateEventHandlerGenerator : IIncrementalGenerator
{
    internal const string MethodName = "On";

    internal static readonly DiagnosticDescriptor Rask179 = new(
        "RASK179",
        "An aggregate's On is not an event handler",
        "'{0}.On' {1} — an aggregate's event handler is 'static Task On(E e)' with E an IEvent",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A method named On on an aggregate is its durable handler of the event it takes: Rask stores a row "
                     + "for it in the outbox when the event is raised and runs it from there, in its own transaction. "
                     + "That needs a static method (no aggregate is loaded when it runs — it loads what it changes), "
                     + "returning Task, taking exactly one parameter whose type implements Rask.Cqrs.IEvent.",
        helpLinkUri: DiagnosticHelp.Link("RASK179"));

    internal static readonly DiagnosticDescriptor Rask188 = new(
        "RASK191",
        "An aggregate's event handler cannot be registered",
        "'{0}' handles '{1}' in On, but {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Rask registers an aggregate's On(E) from a part of the aggregate's own class, so the method can stay "
                     + "private. That needs a partial, non-generic aggregate declared directly in a namespace.",
        helpLinkUri: DiagnosticHelp.Link("RASK191"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { Identifier.ValueText: MethodName },
                static (ctx, token) => Describe(ctx, token))
            .Where(static handler => handler is not null)
            .Select(static (handler, _) => handler!);

        context.RegisterSourceOutput(handlers.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static Handler? Describe(GeneratorSyntaxContext ctx, CancellationToken token)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)ctx.Node, token) is not IMethodSymbol method ||
            method.ContainingType is not { TypeKind: TypeKind.Class } aggregate ||
            !AggregateShape.IsAggregate(aggregate))
        {
            return null;
        }

        var eventType = method.Parameters.Length == 1 && IsEvent(method.Parameters[0].Type) ? method.Parameters[0].Type : null;

        // Meant as a handler only when it takes an event or answers a Task: an On(…) that builds the aggregate is a factory.
        if (eventType is null && !ReturnsTask(method))
        {
            return null;
        }

        return new Handler(
            aggregate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            aggregate.Name,
            aggregate.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            eventType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            ShapeProblem(method, eventType),
            AggregatePart.Problem(aggregate, token),
            SymbolLocation.From(method));
    }

    // Why the method is not 'static Task On(E e)', or null when it is.
    private static string? ShapeProblem(IMethodSymbol method, ITypeSymbol? eventType)
    {
        if (!method.IsStatic)
        {
            return "is not static (no aggregate is loaded when it runs: it loads the one it changes)";
        }

        if (method.IsGenericMethod)
        {
            return "is generic";
        }

        if (!ReturnsTask(method))
        {
            return $"returns '{method.ReturnType.ToDisplayString()}', not Task";
        }

        if (eventType is null)
        {
            return method.Parameters.Length == 1
                ? $"takes '{method.Parameters[0].Type.ToDisplayString()}', which is not an IEvent"
                : $"takes {method.Parameters.Length} parameters, not one event";
        }

        return method.Parameters[0].RefKind == RefKind.None ? null : "takes its event by reference";
    }

    /// <summary>Whether <paramref name="method" /> is an aggregate's event handler of exactly the shape RASK179 asks for.</summary>
    internal static bool IsHandler(IMethodSymbol method) =>
        string.Equals(method.Name, MethodName, System.StringComparison.Ordinal) &&
        method.ContainingType is { TypeKind: TypeKind.Class } aggregate &&
        AggregateShape.IsAggregate(aggregate) &&
        method.Parameters.Length == 1 &&
        IsEvent(method.Parameters[0].Type) &&
        ShapeProblem(method, method.Parameters[0].Type) is null;

    private static bool ReturnsTask(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { Name: "Task", Arity: 0 } returned &&
        string.Equals(returned.ContainingNamespace.ToDisplayString(), "System.Threading.Tasks", System.StringComparison.Ordinal);

    private static bool IsEvent(ITypeSymbol type) =>
        type.AllInterfaces.Any(static i => i is { Name: "IEvent", Arity: 0 } && string.Equals(i.ContainingNamespace.ToDisplayString(), "Rask.Cqrs", System.StringComparison.Ordinal));

    private static void Emit(SourceProductionContext context, ImmutableArray<Handler> all)
    {
        foreach (var aggregate in all.GroupBy(static h => h.AggregateFqn, System.StringComparer.Ordinal).OrderBy(static g => g.Key, System.StringComparer.Ordinal))
        {
            var valid = new List<Handler>();
            foreach (var handler in aggregate)
            {
                if (handler.Shape is { } shape)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rask179, handler.Location?.ToLocation(), handler.AggregateName, shape));
                }
                else if (handler.Unregistrable is { } registration)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rask188, handler.Location?.ToLocation(), handler.AggregateName, handler.EventFqn.Replace("global::", ""), registration));
                }
                else
                {
                    valid.Add(handler);
                }
            }

            if (valid.Count > 0)
            {
                context.AddSource(
                    aggregate.Key.Replace("global::", "") + ".On.g.cs",
                    SourceText.From(Render(valid.OrderBy(static h => h.EventFqn, System.StringComparer.Ordinal).ToList()), Encoding.UTF8));
            }
        }
    }

    private static string Render(List<Handler> handlers)
    {
        var first = handlers[0];
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (first.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(first.Namespace).AppendLine(";");
            s.AppendLine();
        }

        s.Append("partial class ").AppendLine(first.AggregateName);
        s.AppendLine("{");
        s.AppendLine("    /// <summary>Registers this aggregate's <c>On</c> handlers as durable handlers of their events.</summary>");
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        s.AppendLine("    internal static void __RaskRegisterOn() =>");
        s.Append("        global::Rask.Cqrs.CqrsRegistry.ReplaceDurableHandlers(typeof(").Append(first.AggregateFqn).AppendLine("),");
        s.AppendLine("            new (global::System.Type, global::System.Type, global::Rask.Cqrs.CqrsRegistry.EventInvoker)[]");
        s.AppendLine("            {");
        foreach (var handler in handlers)
        {
            s.Append("                (typeof(").Append(handler.EventFqn).Append("), typeof(").Append(handler.AggregateFqn).AppendLine("),");
            s.Append("                    static (provider, e, ct) => global::Rask.Cqrs.AggregateEvents.Handle<")
                .Append(handler.AggregateFqn).Append(", ").Append(handler.EventFqn).Append(">(provider, (")
                .Append(handler.EventFqn).Append(")e, static ev => ").Append(MethodName).AppendLine("(ev), ct)),");
        }

        s.AppendLine("            });");
        s.AppendLine("}");
        return s.ToString();
    }

    private sealed record Handler(
        string AggregateFqn,
        string AggregateName,
        string Namespace,
        string EventFqn,
        string? Shape,
        string? Unregistrable,
        SymbolLocation? Location);
}
