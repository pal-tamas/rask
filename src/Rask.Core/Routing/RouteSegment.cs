namespace Rask.Core.Routing;

internal readonly record struct RouteSegment(SegmentKind Kind, string Literal, string ParamName, bool Optional);
