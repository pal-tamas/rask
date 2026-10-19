namespace Rask.Site.Features;

public sealed partial class PropsIdClassStyleDemo : Component
{
    protected override Component? Render() =>
        Div
            .Id("card-1")
            .Class("rounded-xl border border-zinc-900/10 bg-white shadow-xs ring-2 ring-ui-brand dark:bg-white/10")
            .Style("padding: 0.6rem 0.8rem;")["Three attributes — id then class then style."];
}
