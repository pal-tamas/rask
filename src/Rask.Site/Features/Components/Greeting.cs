namespace Rask.Site.Features;

// A component is a class that subclasses Component and overrides Render.
// The Rask source generator emits a Generated.Greeting(...) factory whose
// parameters are derived from the public settable properties:
//   • Name  — non-nullable, no initializer → required factory parameter.
//   • Title — nullable                     → optional, defaults to null.
public sealed partial class Greeting : Component
{
    public required string Name { get; set; }
    public new string? Title { get; set; }

    protected override Component? Render() =>
        P.Class("mb-0")[
            Title is null ? "" : $"{Title} ",
            "Hello, ", Strong[Name], "!"
        ];
}
