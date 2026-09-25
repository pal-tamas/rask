namespace Rask.Site.Features;

// Call site: invoke the generated factory by its bare name — it is globally
// visible through an auto-generated `global using static`, no using needed.
public sealed partial class ComponentsGreetingDemo : Component
{
    protected override Component? Render() => Greeting.Name("Ada").Title("Dr.");
}
