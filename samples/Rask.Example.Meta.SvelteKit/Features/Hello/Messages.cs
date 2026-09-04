using Rask.Cqrs;

namespace Rask.Example.Meta.SvelteKit.Features.Hello;

/// <summary>The greeting the front end asks for on load.</summary>
/// <remarks>
///     Every public property becomes a TypeScript type at build time. SeenAt is a DateTimeOffset
///     rather than a DateTime on purpose: it carries its offset onto the wire, so the browser reads
///     an unambiguous instant. A bare DateTime would arrive as a local time on whichever machine
///     parsed it.
/// </remarks>
public sealed record Greeting(string Message, DateTimeOffset SeenAt, int Visits);

public sealed record GetGreeting(string Name) : IQuery<Greeting>;

public sealed record RecordVisit(string Name) : ICommand<int>;
