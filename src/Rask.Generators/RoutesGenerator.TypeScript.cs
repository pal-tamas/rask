using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Generators;

public sealed partial class RoutesGenerator
{
    /// <summary>
    ///     The <c>@rask/routes</c> module: this project's <c>Routes</c> class, for its front-end code.
    /// </summary>
    /// <remarks>
    ///     Built from the same candidates, the same tree and the same resolved templates the C# class is — a
    ///     generator never sees another generator's output, so the island generator calls this with the
    ///     compilation instead of reading <c>Routes.g.cs</c>. Nothing is reported from here: every diagnostic a
    ///     page can raise is already raised where the C# is emitted, and a page that cannot be built is left out.
    /// </remarks>
    internal static string TypeScript(Compilation compilation)
    {
        var symbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var candidates = ImmutableArray.CreateBuilder<Candidate>();
        foreach (var type in SymbolWalk.AllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (GetCandidate(type, compilation) is { } candidate)
            {
                symbols[candidate.FullyQualifiedName] = type;
                candidates.Add(candidate);
            }
        }

        var byFqn = DropDuplicateNotFound(Unreported, DropAmbiguous(Unreported, candidates.ToImmutable()))
            .GroupBy(c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var root = BuildRoutesTree(Unreported, byFqn.Values.Where(c => !c.IsNotFound).ToList(), "Routes");

        var types = new TypeScriptTypes(compilation);
        var table = new StringBuilder();
        AppendTypeScriptNode(table, root, byFqn, symbols, types, "  ");

        var sb = new StringBuilder(TypeScriptRuntime.Replace("\r\n", "\n"));
        sb.Append('\n').Append(types.Aliases);
        sb.Append("/** This project's pages — the C# `Routes` class, name for name. */\n");
        sb.Append("export const Routes = {\n").Append(table).Append("}\n");
        return sb.ToString();
    }

    private static void Unreported(Diagnostic diagnostic)
    {
        // Already reported, once, where the C# `Routes` class is emitted.
    }

    private static void AppendTypeScriptNode(StringBuilder sb, RoutesNode node, Dictionary<string, Candidate> byFqn,
        Dictionary<string, INamedTypeSymbol> symbols, TypeScriptTypes types, string indent)
    {
        foreach (var page in node.Pages.OrderBy(c => c.TypeName, StringComparer.Ordinal))
        {
            if (ResolveRoute(Unreported, page, byFqn) is { } route)
            {
                AppendTypeScriptRoute(sb, page, route, symbols[page.FullyQualifiedName], types, indent);
            }
        }

        foreach (var child in node.Children.Values)
        {
            sb.Append(indent).Append(child.Name.TrimStart('@')).Append(": {\n");
            AppendTypeScriptNode(sb, child, byFqn, symbols, types, indent + "  ");
            sb.Append(indent).Append("},\n");
        }
    }

    // One page: its parameters as ONE object keyed by the C# parameter names, and its URL written piece for
    // piece the way EmitFactoryBody writes it — literals verbatim, values through the formatter of their type.
    private static void AppendTypeScriptRoute(StringBuilder sb, Candidate page, ResolvedRoute route,
        INamedTypeSymbol symbol, TypeScriptTypes types, string indent)
    {
        var members = new List<string>();
        var path = new StringBuilder();
        var required = false;

        foreach (var part in route.Parts)
        {
            if (part is LiteralPart literal)
            {
                path.Append('/').Append(literal.Value.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${"));
            }
            else if (part is ParamPart { Optional: var optional } named)
            {
                var prop = route.PathParams.First(p => p.Part == named).Prop;
                var (type, format) = types.For(symbol, prop.Name);
                var nullable = optional || (named.Constraint is null && prop.IsNullable);
                required |= !optional;
                members.Add(prop.Name + (optional ? "?: " : ": ") + type + (nullable ? " | null" : string.Empty));
                path.Append(optional ? "${url.optional(p." : "/${url.segment(p.")
                    .Append(prop.Name).Append(", format.").Append(format).Append(")}");
            }
        }

        var arguments = new List<string> { "`" + path + "`" };
        foreach (var prop in route.QueryProps)
        {
            var (type, format) = types.For(symbol, prop.Name);
            var key = TypeScriptString(Uri.EscapeDataString(prop.QueryParamName ?? prop.Name));
            members.Add($"{prop.Name}?: {type} | null");
            arguments.Add($"url.{(prop.IsNullable ? "given" : "always")}({key}, p.{prop.Name}, format.{format})");
        }

        // A page every parameter of which can be left out is called with no argument at all, as in C#.
        var parameters = string.Empty;
        if (members.Count > 0)
        {
            parameters = "p: { " + string.Join("; ", members) + " }" + (required ? string.Empty : " = {}");
        }

        sb.Append(indent).Append(page.TypeName).Append(": (").Append(parameters).Append("): Route =>\n");
        sb.Append(indent).Append("  route(").Append(string.Join(", ", arguments)).Append("),\n");
    }

    private static string TypeScriptString(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>What a route parameter's C# type is in TypeScript, and which formatter writes it into a URL.</summary>
    /// <remarks>
    ///     The type is the island prop mapping's — <see cref="TypeScriptEmitter" /> over <see cref="WireShape" /> —
    ///     so a <c>Guid</c> or a <c>DateOnly</c> is spelled one way in <c>@rask/Chart.props</c> and here. A type
    ///     with no scalar mapping — the author's own <c>IParsable&lt;T&gt;</c> — is a <c>string</c> the caller has
    ///     already formatted: only its C# <c>ToString()</c> knows how it is written.
    /// </remarks>
    private sealed class TypeScriptTypes(Compilation compilation)
    {
        private static readonly string[] StringAliases = ["Guid", "DateOnly", "TimeOnly", "Duration"];

        private readonly TypeScriptEmitter _emitter = new();
        private readonly SortedSet<string> _aliases = new(StringComparer.Ordinal);

        /// <summary>The declarations of the string aliases the table used, each followed by a blank line.</summary>
        public string Aliases =>
            _aliases.Count == 0
                ? string.Empty
                : string.Concat(_aliases.Select(alias => $"export type {alias} = string\n")) + "\n";

        public (string Type, string Format) For(INamedTypeSymbol page, string property)
        {
            var type = GetUnderlying(page.GetMembers(property).OfType<IPropertySymbol>().First().Type);
            var wire = WireShape.Classify(type, allowFile: false, compilation: compilation);
            var mapped = wire.Kind == WireKind.Scalar ? _emitter.Ensure(wire) : "string";
            if (Array.IndexOf(StringAliases, mapped) >= 0)
            {
                _aliases.Add(mapped);
            }

            return mapped switch
            {
                "number" => (mapped, "number"),
                "boolean" => (mapped, "boolean"),
                "DateOnly" => (mapped, "date"),
                "TimeOnly" => (mapped, "time"),
                "Date" => (mapped, wire.Fqn.EndsWith("DateTimeOffset", StringComparison.Ordinal) ? "offset" : "stamp"),
                "unknown" => ("string", "text"),
                _ => (mapped, "text"),
            };
        }
    }
}
