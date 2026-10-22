namespace Rask.Site.Features;

public sealed partial class TagsTextDemo : Component
{
    protected override Component? Render() => Article[
        H1.Class("text-xl font-semibold")["Tags are just methods."],
        P[
            "You can ", Strong["emphasize"], " or ", Em["italicize"],
            " by composing them."
        ],
        Blockquote.Class("border-l-4 border-zinc-200 dark:border-white/10 pl-4 text-base italic text-zinc-500 dark:text-white/70")["A small DSL, an honest day's HTML."]
    ];
}
