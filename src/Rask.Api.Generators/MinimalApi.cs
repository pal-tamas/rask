using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Api.Generators;

/// <summary>
///     One <c>app.MapGet(...)</c>-style registration, reduced to what the generator can read from it.
/// </summary>
/// <param name="Verb">The HTTP method the Map call names.</param>
/// <param name="Pattern">The route pattern, which must be a compile-time constant.</param>
/// <param name="Handler">The lambda or method group that answers it.</param>
/// <param name="Name">The name from a chained <c>.WithName("…")</c>, when there is one.</param>
/// <param name="Site">Where to report a diagnostic about it.</param>
internal sealed record MinimalApiRegistration(
    string Verb,
    string Pattern,
    IMethodSymbol Handler,
    string? Name,
    Location Site);

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
        var declared = target.ReducedFrom ?? target;

        // Matched structurally rather than by assembly identity, so the generator needs no reference to
        // ASP.NET and a re-homed extension method does not silently stop being seen.
        if (declared.ContainingType?.Name != "EndpointRouteBuilderExtensions" ||
            declared.Parameters.Length < 2 ||
            declared.Parameters[0].Type.Name != "IEndpointRouteBuilder")
        {
            return null;
        }

        var verb = target.Name switch
        {
            "MapGet" => "GET",
            "MapPost" => "POST",
            "MapPut" => "PUT",
            "MapPatch" => "PATCH",
            "MapDelete" => "DELETE",
            _ => null,
        };

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

    // A stand-in so the "unreadable" case can still travel as a record rather than as a null the caller
    // has to remember to check twice.
    private static IMethodSymbol NullHandler => null!;

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

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
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
