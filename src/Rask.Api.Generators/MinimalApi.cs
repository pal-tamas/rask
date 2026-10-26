using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Api.Generators;

/// <summary>
///     Finds minimal API registrations in source.
/// </summary>
/// <remarks>
///     Only the analyzable shapes are accepted, and everything else is refused with RASK068 rather than
///     guessed at. A pattern built at run time, or a handler whose parameters have no declared types,
///     cannot be turned into a method signature — and a client that builds <em>almost</em> the right URL
///     is worse than no client at all, because it type-checks on both sides and fails as a 404.
/// </remarks>
internal static class MinimalApi
{
    private static readonly string[] Methods =
        ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];

    /// <summary>Whether a node is worth a semantic look.</summary>
    public static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
        && Array.IndexOf(Methods, member.Name.Identifier.ValueText) >= 0;

    /// <summary>Reads a candidate, or returns null when it is not a shape the generator supports.</summary>
    public static MinimalApiRegistration? Read(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol target)
        {
            return null;
        }

        // ReducedFrom, because `endpoints.MapGet(...)` binds to the REDUCED extension method, whose
        // Parameters no longer include the receiver — so checking Parameters[0] on the reduced symbol
        // inspects the route pattern and never matches. This is silent: the generator simply produces
        // nothing, which reads as "minimal APIs are not supported" rather than as a bug.
        if (!IsMapExtension(target.ReducedFrom ?? target))
        {
            return null;
        }

        // An endpoint the app took out of its API description is not part of its API: no client method,
        // and no RASK068/RASK070 for an endpoint it never meant a client to call. This is the same switch
        // OpenAPI honours, so one call answers both readers — on the endpoint or on the group it is in.
        if (DescriptionExclusion.IsExcluded(model, invocation))
        {
            return null;
        }

        // A Map* whose receiver is a MapGroup result carries a prefix this call site cannot see: the
        // group's pattern lives on the RouteGroupBuilder, and the emitted client would call "/{id}"
        // where the server answers "/api/widgets/{id}". That is the "almost right URL" this generator
        // exists to refuse, so the endpoint is skipped and reported rather than guessed at.
        if (IsGrouped(model, invocation))
        {
            return new MinimalApiRegistration(
                target.Name, GroupedMarker, NullHandler, null, invocation.GetLocation());
        }

        var verb = VerbOf(target.Name);
        if (verb is null || invocation.ArgumentList.Arguments.Count < 2)
        {
            return null;
        }

        var pattern = model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression);

        if (!pattern.HasValue || pattern.Value is not string route)
        {
            // An interpolated or computed pattern. Reported, because the endpoint plainly exists and an
            // author who sees no client method deserves to know why.
            return new MinimalApiRegistration(verb, string.Empty, NullHandler, null, invocation.GetLocation());
        }

        var handler = Handler(model, invocation.ArgumentList.Arguments[1].Expression);

        return new MinimalApiRegistration(
            verb, route, handler!, Name(invocation), invocation.GetLocation());
    }

    // Matched structurally rather than by assembly identity, so the generator needs no reference to
    // ASP.NET and a re-homed extension method does not silently stop being seen.
    private static bool IsMapExtension(IMethodSymbol declared) =>
        string.Equals(declared.ContainingType?.Name, "EndpointRouteBuilderExtensions", StringComparison.Ordinal) &&
        declared.Parameters.Length >= 2 &&
        string.Equals(declared.Parameters[0].Type.Name, "IEndpointRouteBuilder", StringComparison.Ordinal);

    private static string? VerbOf(string mapMethod) => mapMethod switch
    {
        "MapGet" => "GET",
        "MapPost" => "POST",
        "MapPut" => "PUT",
        "MapPatch" => "PATCH",
        "MapDelete" => "DELETE",
        _ => null,
    };

    /// <summary>The pattern a grouped registration carries, so the caller can report it by name.</summary>
    public const string GroupedMarker = "\0grouped";

    // A stand-in so the "unreadable" case can still travel as a record rather than as a null the caller
    // has to remember to check twice.
    private static IMethodSymbol NullHandler => null!;

    /// <summary>
    ///     Whether this <c>Map*</c> hangs off a <c>MapGroup(...)</c> rather than the app itself.
    /// </summary>
    /// <remarks>
    ///     Detected on the receiver's TYPE rather than by looking for the name <c>MapGroup</c>: a group
    ///     is routinely held in a local (<c>var admin = app.MapGroup("/admin"); admin.MapGet(...)</c>),
    ///     and a syntax-only check would see <c>admin</c> and miss it — emitting a client for a URL the
    ///     server never answers, which is worse than emitting none.
    /// </remarks>
    private static bool IsGrouped(SemanticModel model, InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member
        && model.GetTypeInfo(member.Expression).Type is { Name: "RouteGroupBuilder" };

    private static IMethodSymbol? Handler(SemanticModel model, ExpressionSyntax expression)
    {
        switch (expression)
        {
            // A lambda with typed parameters. An implicitly-typed one (`x => …`) has no declared types to
            // build a signature from, so GetSymbolInfo's symbol is used only when every parameter has one.
            case ParenthesizedLambdaExpressionSyntax lambda:
                var symbol = model.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
                return lambda.ParameterList.Parameters.All(p => p.Type is not null) ? symbol : null;

            case SimpleLambdaExpressionSyntax:
                return null;

            // A method group: `app.MapGet("/x", Handlers.GetThing)`.
            default:
                return model.GetSymbolInfo(expression).Symbol as IMethodSymbol
                    ?? model.GetSymbolInfo(expression).CandidateSymbols.OfType<IMethodSymbol>()
                        .FirstOrDefault();
        }
    }

    // .WithName("…") anywhere in the chain hanging off this call.
    private static string? Name(InvocationExpressionSyntax invocation)
    {
        for (SyntaxNode? node = invocation.Parent; node is not null; node = node.Parent)
        {
            if (node is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WithName" },
                } chained)
            {
                if (node is InvocationExpressionSyntax or MemberAccessExpressionSyntax)
                {
                    continue;
                }

                return null;
            }

            if (chained.ArgumentList.Arguments.Count == 1 &&
                chained.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                literal.Token.Value is string name &&
                IsIdentifier(name))
            {
                return name;
            }

            return null;
        }

        return null;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    /// <summary>
    ///     The client class a minimal API endpoint belongs to, taken from the route.
    /// </summary>
    /// <remarks>
    ///     A minimal API has no controller to be named after, and most live in <c>Program.cs</c>, whose
    ///     enclosing type is <c>Program</c> — a name that would tell a reader nothing. The route's own
    ///     first meaningful segment does: <c>/api/items/{id}</c> groups into <c>ItemsClient</c>, next to
    ///     every other endpoint under <c>/api/items</c>.
    /// </remarks>
    public static string ClientName(string route)
    {
        foreach (var segment in route.Split('/'))
        {
            if (segment.Length == 0 || segment[0] == '{' ||
                segment.Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cleaned = new string([.. segment.Where(char.IsLetterOrDigit)]);

            if (cleaned.Length > 0 && char.IsLetter(cleaned[0]))
            {
                return Pascal(cleaned) + "Client";
            }
        }

        return "ApiClient";
    }

    /// <summary>
    ///     The client method name, derived from the verb and the route past the grouping segment.
    /// </summary>
    /// <remarks>
    ///     Deterministic rather than clever, and a collision is reported as RASK069 telling the author to
    ///     add <c>.WithName("…")</c> — which is ASP.NET's own way of naming an endpoint, so the fix is
    ///     something the author would recognise rather than a Rask invention.
    /// </remarks>
    public static string MethodName(string verb, string route)
    {
        var builder = new StringBuilder(Pascal(verb.ToLowerInvariant()));
        var group = false;

        foreach (var segment in route.Split('/'))
        {
            if (segment.Length == 0 || segment.Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (segment[0] == '{')
            {
                var token = RouteTemplate.Tokens("{" + segment.Trim('{', '}') + "}");

                if (token.Count > 0)
                {
                    builder.Append("By").Append(Pascal(token[0]));
                }

                continue;
            }

            // The first literal segment named the client; repeating it in every method would read as
            // ItemsClient.GetItems().
            if (!group)
            {
                group = true;
                continue;
            }

            builder.Append(Pascal(new string([.. segment.Where(char.IsLetterOrDigit)])));
        }

        return builder.ToString();
    }

    private static string Pascal(string value) =>
        value.Length == 0
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value.Substring(1);
}
