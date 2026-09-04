namespace Rask.Example.Shared.Features;

public sealed partial class TagsTextDemo : Component
{
    protected override Component? Render() => Article[
        H1.Class("text-xl font-semibold")["Tags are just methods."],
        P[
            "You can ", Strong["emphasize"], " or ", Em["italicize"],
            " by composing them."
        ],
        Blockquote.Class($"{Tw.Blockquote} text-base")["A small DSL, an honest day's HTML."]
    ];
}
