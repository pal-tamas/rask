namespace Rask.Core.Routing;

// `Holds` is what a `{id:int}` segment asks of the text in its place, or null: a segment that is not one does not
// match (RoutePattern.MatchSegment).
internal readonly record struct RouteSegment(
    SegmentKind Kind, string Literal, string ParamName, bool Optional, Func<string, bool>? Holds = null);
