namespace Rask.Site.Features;

public sealed partial class TierStatelessGreetingDemo : Component
{
    protected override Component? Render() => TierGreeting.Name("Ada");
}
