using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Api.Generators;

/// <summary>
///     Resolves an action's route the way ASP.NET does, far enough for a client to build the URL.
/// </summary>
/// <remarks>
///     Deliberately a small subset, and everything outside it is refused rather than guessed. A client
///     that builds <em>almost</em> the right URL is the worst outcome available here: it type-checks on
///     both sides and fails as a 404 in production, which is why the unsupported shapes report RASK062
///     instead of being approximated.
/// </remarks>
internal static class RouteTemplate
{
    private const string MvcNamespace = "Microsoft.AspNetCore.Mvc";

    /// <summary>The class-level template, inherited up the base chain, or empty.</summary>
    public static string OfController(INamedTypeSymbol controller)
    {
        for (var current = controller; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name != "RouteAttribute" ||
                    attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() != MvcNamespace)
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length > 0 &&
                    attribute.ConstructorArguments[0].Value is string template)
                {
                    return template;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Every (verb, template) pair the action declares. An action may answer more than one.</summary>
    public static IEnumerable<(string Verb, string Template)> OfAction(IMethodSymbol action)
    {
        foreach (var attribute in action.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;

            if (attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() != MvcNamespace)
            {
                continue;
            }

            var verb = name switch
            {
                "HttpGetAttribute" => "GET",
                "HttpPostAttribute" => "POST",
                "HttpPutAttribute" => "PUT",
                "HttpPatchAttribute" => "PATCH",
                "HttpDeleteAttribute" => "DELETE",
                _ => null,
            };

            if (verb is null)
            {
                continue;
            }

            var template = attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string declared
                ? declared
                : string.Empty;

            yield return (verb, template);
        }
    }

    /// <summary>
    ///     Combines the class and method templates and substitutes the standard tokens.
    /// </summary>
    /// <remarks>
    ///     A method template starting <c>/</c> or <c>~/</c> replaces the class template rather than
    ///     appending to it, which is ASP.NET's rule and the one an author relies on to put a single
    ///     action outside the controller's prefix.
    /// </remarks>
    public static string Combine(
        string controllerTemplate,
        string actionTemplate,
        INamedTypeSymbol controller,
        IMethodSymbol action)
    {
        string combined;

        if (actionTemplate.StartsWith("~/", StringComparison.Ordinal))
        {
            combined = actionTemplate.Substring(1);
        }
        else if (actionTemplate.StartsWith("/", StringComparison.Ordinal))
        {
            combined = actionTemplate;
        }
        else if (controllerTemplate.Length == 0)
        {
            combined = actionTemplate;
        }
        else if (actionTemplate.Length == 0)
        {
            combined = controllerTemplate;
        }
        else
        {
            combined = controllerTemplate.TrimEnd('/') + "/" + actionTemplate.TrimStart('/');
        }

        var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            && controller.Name.Length > 10
            ? controller.Name.Substring(0, controller.Name.Length - "Controller".Length)
            : controller.Name;

        // Case-insensitively, because ASP.NET's own token replacement is. `[Controller]` is as legal as
        // `[controller]`, and leaving it verbatim would put a literal bracket in the URL.
        combined = ReplaceToken(combined, "controller", controllerName);
        combined = ReplaceToken(combined, "action", action.Name);

        if (!combined.StartsWith("/", StringComparison.Ordinal))
        {
            combined = "/" + combined;
        }

        return combined.Length > 1 ? combined.TrimEnd('/') : combined;
    }

    /// <summary>Replaces <c>[name]</c> case-insensitively, the way ASP.NET's own routing does.</summary>
    private static string ReplaceToken(string route, string token, string value)
    {
        var needle = "[" + token + "]";

        for (var at = route.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = route.IndexOf(needle, StringComparison.OrdinalIgnoreCase))
        {
            route = route.Substring(0, at) + value + route.Substring(at + needle.Length);
        }

        return route;
    }

    /// <summary>
    ///     A route token this generator does not substitute, or null when every one was resolved.
    /// </summary>
    /// <remarks>
    ///     <c>[area]</c> is the live example. Left verbatim it becomes a literal <c>[area]</c> in the
    ///     client's URL — escaped nowhere, matched by nothing, and a 404 at run time with no diagnostic
    ///     to explain it. That is the "almost right URL" this class refuses everywhere else, so it is
    ///     reported rather than emitted.
    /// </remarks>
    public static string? UnresolvedToken(string route)
    {
        var open = route.IndexOf('[');

        if (open < 0)
        {
            return null;
        }

        var close = route.IndexOf(']', open);
        return close < 0 ? null : route.Substring(open, close - open + 1);
    }

    /// <summary>
    ///     The bare parameter names a template asks for — <c>id</c> from <c>{id:int}</c>, <c>{id?}</c> or
    ///     <c>{id=1}</c>.
    /// </summary>
    public static IReadOnlyList<string> Tokens(string route)
    {
        var tokens = new List<string>();
        var start = -1;

        for (var i = 0; i < route.Length; i++)
        {
            if (route[i] == '{')
            {
                start = i + 1;
            }
            else if (route[i] == '}' && start >= 0)
            {
                var name = Bare(route.Substring(start, i - start));

                if (name.Length > 0)
                {
                    tokens.Add(name);
                }

                start = -1;
            }
        }

        return tokens;
    }

    /// <summary>
    ///     Rewrites the template with every token reduced to its bare name, so the emitter can substitute
    ///     by simple text replacement.
    /// </summary>
    public static string Bareize(string route)
    {
        var builder = new StringBuilder(route.Length);
        var start = -1;

        for (var i = 0; i < route.Length; i++)
        {
            if (route[i] == '{')
            {
                start = i + 1;
                builder.Append('{');
            }
            else if (route[i] == '}' && start >= 0)
            {
                builder.Append(Bare(route.Substring(start, i - start))).Append('}');
                start = -1;
            }
            else if (start < 0)
            {
                builder.Append(route[i]);
            }
        }

        return builder.ToString();
    }

    // "id:int=3" -> "id", "*rest" -> "rest". The constraint and default matter to the server's matcher
    // and mean nothing to a client, which substitutes a value it already holds.
    private static string Bare(string token)
    {
        var name = token;

        var colon = name.IndexOf(':');
        if (colon >= 0)
        {
            name = name.Substring(0, colon);
        }

        var equals = name.IndexOf('=');
        if (equals >= 0)
        {
            name = name.Substring(0, equals);
        }

        return name.TrimStart('*').TrimEnd('?').Trim();
    }
}
