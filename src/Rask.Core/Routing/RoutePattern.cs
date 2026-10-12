using System.Globalization;

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
            segments[i] = ParseSegment(parts[i], template);
            if (segments[i].Kind == SegmentKind.Literal)
            {
                literal++;
            }
        }

        return new RoutePattern(template, segments, literal);
    }

    // These are the runtime siblings of RASK003, and they used to be worse off than it: they echoed the
    // offending segment, never showed a correct one, and — unlike the diagnostic — carried no way to tell
    // WHICH route it came from, so `Empty parameter in route segment '{}'` was the whole story. The
    // template is threaded in for that reason.
    private static RouteSegment ParseSegment(string raw, string template)
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
            throw new InvalidOperationException(
                $"Route template '{template}' has an empty parameter segment '{raw}'. Give the "
                + "parameter a name — '{id}' — or make the segment a literal by dropping the braces.");
        }

        if (inner.StartsWith("**", StringComparison.Ordinal))
        {
            return CatchAll(inner[2..], "**", raw, template);
        }

        if (inner.StartsWith('*'))
        {
            return CatchAll(inner[1..], "*", raw, template);
        }

        return Parameter(inner, raw, template);
    }

    private static RouteSegment CatchAll(string name, string stars, string raw, string template)
    {
        if (name.Length == 0)
        {
            throw new InvalidOperationException(
                $"Route template '{template}' has an unnamed catch-all segment '{raw}'. Name it — "
                + $"'{{{stars}path}}' — so the matched remainder has something to bind to.");
        }

        return new RouteSegment(SegmentKind.CatchAll, string.Empty, name, true);
    }

    private static RouteSegment Parameter(string inner, string raw, string template)
    {
        var optional = inner[^1] == '?';
        var paramName = optional ? inner[..^1] : inner;
        // A type constraint (`{id:guid}`, `{count:int}`, …) types the generated URL and is held to here: a
        // segment that is not one — letters, digits past what an int holds — does not match, and the path
        // falls to the next route or to not-found. The suffix is cut off the name, which is the binding
        // name (`Id`, `Count`, …) PageBinder looks up in the values.
        Func<string, bool>? holds = null;
        var colon = paramName.IndexOf(':');
        if (colon >= 0)
        {
            holds = Constraint(paramName[(colon + 1)..]);
            paramName = paramName[..colon];
        }

        if (paramName.Length == 0)
        {
            throw new InvalidOperationException(
                $"Route template '{template}' has an unnamed parameter segment '{raw}'. Put the name "
                + "before the constraint and the '?' — '{id:guid?}', not '{:guid?}'.");
        }

        return new RouteSegment(SegmentKind.Parameter, string.Empty, paramName, optional, holds);
    }

    // The four the generator types a URL by (RoutesGenerator.PathParamTypeFqn). Any other word is a string.
    // Read as PageBinder reads the value into the property (IParsable, invariant): a sign and leading zeros
    // are a number, a thousands separator or a decimal point is not.
    private static Func<string, bool>? Constraint(string constraint) => constraint switch
    {
        "int" => static text => int.TryParse(text, CultureInfo.InvariantCulture, out _),
        "long" => static text => long.TryParse(text, CultureInfo.InvariantCulture, out _),
        "bool" => static text => bool.TryParse(text, out _),
        "guid" => static text => Guid.TryParse(text, out _),
        _ => null,
    };

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
                values[seg.ParamName] = pi >= pathSegments.Length
                    ? null
                    : Uri.UnescapeDataString(string.Join('/', pathSegments, pi, pathSegments.Length - pi));
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

            if (!MatchSegment(seg, pathSegments[pi], values))
            {
                values.Clear();
                return false;
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

    // A literal must match (case-insensitively); a parameter does when it can be read as what its constraint
    // says it is, and records what it matched.
    private static bool MatchSegment(RouteSegment seg, string current, Dictionary<string, string?> values)
    {
        if (seg.Kind == SegmentKind.Literal)
        {
            return string.Equals(seg.Literal, current, StringComparison.OrdinalIgnoreCase);
        }

        if (seg.Kind == SegmentKind.Parameter)
        {
            var value = Uri.UnescapeDataString(current);
            if (seg.Holds is { } holds && !holds(value))
            {
                return false;
            }

            values[seg.ParamName] = value;
        }

        return true;
    }
}
