namespace Rask.Core.Routing;

internal sealed class RoutePattern
{
    private RoutePattern(string template, IReadOnlyList<RouteSegment> segments, int literalCount)
    {
        Template = template;
        Segments = segments;
        LiteralSegmentCount = literalCount;
    }

    public string Template { get; }
    public IReadOnlyList<RouteSegment> Segments { get; }
    public int LiteralSegmentCount { get; }

    public static RoutePattern Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var raw = template.Trim('/');
        if (raw.Length == 0)
        {
            return new RoutePattern("/", Array.Empty<RouteSegment>(), 0);
        }

        var parts = raw.Split('/');
        var segments = new RouteSegment[parts.Length];
        var literal = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            segments[i] = ParseSegment(parts[i]);
            if (segments[i].Kind == SegmentKind.Literal)
            {
                literal++;
            }
        }

        return new RoutePattern(template, segments, literal);
    }

    private static RouteSegment ParseSegment(string raw)
    {
        if (raw.Length == 0)
        {
            return new RouteSegment(SegmentKind.Literal, string.Empty, string.Empty, false);
        }

        if (raw[0] != '{' || raw[^1] != '}')
        {
            return new RouteSegment(SegmentKind.Literal, raw, string.Empty, false);
        }

        var inner = raw[1..^1];
        if (inner.Length == 0)
        {
            throw new InvalidOperationException($"Empty parameter in route segment '{raw}'.");
        }

        if (inner.StartsWith("**", StringComparison.Ordinal))
        {
            var name = inner[2..];
            if (name.Length == 0)
            {
                throw new InvalidOperationException($"Catch-all parameter must have a name in '{raw}'.");
            }

            return new RouteSegment(SegmentKind.CatchAll, string.Empty, name, true);
        }

        if (inner.StartsWith('*'))
        {
            var name = inner[1..];
            if (name.Length == 0)
            {
                throw new InvalidOperationException($"Catch-all parameter must have a name in '{raw}'.");
            }

            return new RouteSegment(SegmentKind.CatchAll, string.Empty, name, true);
        }

        var optional = inner[^1] == '?';
        var paramName = optional ? inner[..^1] : inner;
        // Type constraints (`{id:guid}`, `{count:int}`, …) are a generator-side hint —
        // RoutesGenerator parses them for the typed URL-formatter signature and runtime
        // bindability checks, but the live router doesn't enforce them. Strip the
        // `:constraint` suffix here so the segment param name matches the binding name
        // (`Id`, `Count`, …) that PageBinder will look up in the values dictionary.
        var colon = paramName.IndexOf(':');
        if (colon >= 0)
        {
            paramName = paramName[..colon];
        }

        if (paramName.Length == 0)
        {
            throw new InvalidOperationException($"Parameter must have a name in '{raw}'.");
        }

        return new RouteSegment(SegmentKind.Parameter, string.Empty, paramName, optional);
    }

    public bool TryMatch(string path, out Dictionary<string, string?> values)
    {
        values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var trimmed = (path ?? string.Empty).Trim('/');
        var pathSegments = trimmed.Length == 0
            ? Array.Empty<string>()
            : trimmed.Split('/');

        var pi = 0;
        for (var ti = 0; ti < Segments.Count; ti++)
        {
            var seg = Segments[ti];

            if (seg.Kind == SegmentKind.CatchAll)
            {
                if (pi >= pathSegments.Length)
                {
                    values[seg.ParamName] = null;
                    return true;
                }

                var rest = string.Join('/', pathSegments, pi, pathSegments.Length - pi);
                values[seg.ParamName] = Uri.UnescapeDataString(rest);
                return true;
            }

            if (pi >= pathSegments.Length)
            {
                if (seg.Kind == SegmentKind.Parameter && seg.Optional)
                {
                    values[seg.ParamName] = null;
                    continue;
                }

                values.Clear();
                return false;
            }

            var current = pathSegments[pi];
            switch (seg.Kind)
            {
                case SegmentKind.Literal:
                    if (!string.Equals(seg.Literal, current, StringComparison.OrdinalIgnoreCase))
                    {
                        values.Clear();
                        return false;
                    }

                    break;

                case SegmentKind.Parameter:
                    values[seg.ParamName] = Uri.UnescapeDataString(current);
                    break;
            }

            pi++;
        }

        if (pi != pathSegments.Length)
        {
            values.Clear();
            return false;
        }

        return true;
    }
}

internal enum SegmentKind { Literal, Parameter, CatchAll }

internal readonly record struct RouteSegment(SegmentKind Kind, string Literal, string ParamName, bool Optional);
