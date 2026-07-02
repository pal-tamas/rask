namespace Rask.Example.Shared.Features;

public sealed class TagsTextDemo : Component
{
    protected override Component? Render() => Article()[
        H1(Class: "h4")["Tags are just methods."],
        P()[
            "You can ", Strong()["emphasize"], " or ", Em()["italicize"],
            " by composing them."
        ],
        Blockquote(Class: "blockquote fs-6")["A small DSL, an honest day's HTML."]
    ];
}
