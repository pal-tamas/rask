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

        combined = combined
            .Replace("[controller]", controllerName)
            .Replace("[action]", action.Name);

        if (!combined.StartsWith("/", StringComparison.Ordinal))
        {
            combined = "/" + combined;
        }

        return combined.Length > 1 ? combined.TrimEnd('/') : combined;
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
