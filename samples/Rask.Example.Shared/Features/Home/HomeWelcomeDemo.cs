namespace Rask.Example.Shared.Features;

// The minimal page distilled to a self-contained component: a generator-emitted factory
// tree where strings convert implicitly to Component and Component.ToHtml() produces the HTML.
public sealed class HomeWelcomeDemo : Component
{
    protected override Component? Render() =>
        [
            H1(Class: "h3 mb-2")["Hello, world!"],
            P(Class: "text-secondary mb-0")["A page rendered with Rask."]
        ];
}
