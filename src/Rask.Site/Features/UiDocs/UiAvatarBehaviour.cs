namespace Rask.Site.Features.UiDocs;

/// <summary>How an avatar behaves: what it draws without a picture, how it picks a colour, and how a group stacks.</summary>
internal sealed partial class UiAvatarBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Initials(),
            Colour(),
            Group()
        ];

    private static UiDocsTopic Initials() =>
        UiDocsTopic.Key("initials").Heading("Without a picture").Paragraphs([
            "`Src` is optional. Given a `Name`, it shows the first letter of the first and last words (\"Mara Lind\" "
            + "is ML), the first two letters of a single word (\"maralind\" is Ma), or one letter with "
            + "`InitialsSingle()`. `Initials` states them, `Icon` draws an icon instead, and children replace all of "
            + "it: `Ui.Avatar[\"3+\"]`.",
            "`Href` makes it a link and `As(Ui.AvatarAs.Button)` a button; `Tooltip` names it on hover and keyboard "
            + "focus.",
        ]);

    private static UiDocsTopic Colour() =>
        UiDocsTopic.Key("colour").Heading("One person, one colour").Paragraphs([
            "`Color` fills it with one of `Ui.Color`'s hues. `ColorAuto()` picks one from the initials, or from "
            + "`ColorSeed`: the CRC-32 of them over Flux's seventeen hues, so the same person is the same colour here "
            + "as on a Flux page.",
        ]);

    private static UiDocsTopic Group() =>
        UiDocsTopic.Key("group").Heading("Groups").Paragraphs([
            "`Ui.AvatarGroup` stacks its avatars, each over the one before and ringed in the page's colour. On "
            + "another ground, give the ring that ground's colour: `Class(\"*:ring-zinc-100\")`.",
        ]);
}
