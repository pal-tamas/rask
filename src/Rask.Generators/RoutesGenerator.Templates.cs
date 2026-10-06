using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class RoutesGenerator
{
    private static bool TryResolveFullTemplate(Action<Diagnostic> report, Candidate c,
        Dictionary<string, Candidate> byFqn, int leafTemplateIndex, out string fullTemplate)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = c;
        var isLeaf = true;
        while (current is not null)
        {
            if (!seen.Add(current.FullyQualifiedName))
            {
                report(
                    Diagnostic.Create(Rask007, c.RouteAttrLocation.ToLocation(), c.FullyQualifiedName));
                fullTemplate = string.Empty;
                return false;
            }

            // The leaf composes with whichever of its own templates we're resolving (each
            // call to this helper resolves ONE concrete URL). Parents always contribute
            // their first declared [Route] template — multi-route parents would otherwise
            // explode the chain combinatorially, and the URL formatter has no way to pick.
            var localTemplate = isLeaf ? current.Templates[leafTemplateIndex] : current.Templates[0];
            parts.Insert(0, localTemplate);
            isLeaf = false;
            if (current.ParentTypeFqn is null)
            {
                break;
            }

            byFqn.TryGetValue(current.ParentTypeFqn, out var parent);
            current = parent;
        }

        fullTemplate = JoinTemplates(parts);
        return true;
    }

    private static string JoinTemplates(List<string> parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p))
            {
                continue;
            }

            var trimmed = p;
            if (sb.Length > 0)
            {
                if (trimmed.StartsWith("/", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(1);
                }

                if (sb[sb.Length - 1] != '/' && trimmed.Length > 0)
                {
                    sb.Append('/');
                }
            }

            sb.Append(trimmed);
        }

        if (sb.Length == 0)
        {
            return "/";
        }

        if (sb[0] != '/')
        {
            sb.Insert(0, '/');
        }

        return sb.ToString();
    }

    private static bool TryParseTemplate(string template, out List<ITemplatePart> parts, out string error)
    {
        parts = new List<ITemplatePart>();
        error = string.Empty;
        if (string.IsNullOrEmpty(template))
        {
            return true;
        }

        var path = template.StartsWith("/", StringComparison.Ordinal) ? template.Substring(1) : template;
        if (path.Length == 0)
        {
            return true;
        }

        var segs = path.Split('/');
        foreach (var seg in segs)
        {
            if (seg.Length == 0)
            {
                error = "empty segment — remove the doubled '/', e.g. \"/users/{id:int}\"";
                return false;
            }

            if (seg[0] == '{' && seg[seg.Length - 1] == '}')
            {
                if (ParseParam(seg.Substring(1, seg.Length - 2), out error) is not { } param)
                {
                    return false;
                }

                parts.Add(param);
            }
            else if (seg.IndexOf('{') >= 0 || seg.IndexOf('}') >= 0)
            {
                error = "mixed literal/param segments are not supported — give the parameter its own segment, e.g. \"/order/{id}\" rather than \"/order-{id}\"";
                return false;
            }
            else
            {
                parts.Add(new LiteralPart(seg));
            }
        }

        return true;
    }

    // The inside of one `{…}` segment: name, optional `:constraint`, optional trailing `?`.
    private static ParamPart? ParseParam(string inner, out string error)
    {
        error = string.Empty;
        if (inner.StartsWith("**", StringComparison.Ordinal))
        {
            error = "catch-all '{**...}' segments are not supported in typed routes — name the segments you need, e.g. \"/files/{folder}/{name}\", or match the rest inside the page";
            return null;
        }

        var optional = false;
        if (inner.EndsWith("?", StringComparison.Ordinal))
        {
            optional = true;
            inner = inner.Substring(0, inner.Length - 1);
        }

        string name;
        string? constraint = null;
        var colon = inner.IndexOf(':');
        if (colon >= 0)
        {
            name = inner.Substring(0, colon);
            constraint = inner.Substring(colon + 1).ToLowerInvariant();
        }
        else
        {
            name = inner;
        }

        if (name.Length == 0)
        {
            error = "param has no name — name it, e.g. \"/users/{id}\" or \"/users/{id:guid?}\"";
            return null;
        }

        return new ParamPart(name, constraint, optional);
    }

    private static string EscapeForCSharpStringLiteral(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }
}
