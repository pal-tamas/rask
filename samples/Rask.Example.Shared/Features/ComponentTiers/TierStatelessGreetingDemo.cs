namespace Rask.Example.Shared.Features;

// Tier 1 — a stateless Component: a subclass whose Render() is a pure function of its props
// and holds no mutable fields. Public settable props become a generated bare-name factory
// (Name is required because it is non-nullable with no initializer). Unlike a static method it
// gains a reconciliation identity, lifecycle hooks, render caching and safe context reads — it
// just carries no local state of its own.
public sealed class TierGreeting : Component
{
    public required string Name { get; set; }

    protected override Component? Render() =>
        P(Class: "mb-0")["Hello, ", Strong()[Name], "!"];
}

public sealed class TierStatelessGreetingDemo : Component
{
    protected override Component? Render() => TierGreeting(Name: "Ada");
}
