using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's avatar and avatar group as a C# component: each example with its own code, and its reference.</summary>
[Route("avatar")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiAvatarPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Avatar.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Avatar)
            .Lead("A person as a picture, or as initials or an icon when there is none. Give it a Name and it works "
                  + "out the initials, the picture's alternative text and a colour of its own.")
            .Api(UiApi.Avatar)[
            UiDocsExample.Key("ui-avatar").Heading("Avatar").File("AvatarExample.cs").Demo(AvatarExample).Testid("ui-avatar")
                .Blurb("A picture, named by Name."),
            UiDocsExample.Key("ui-avatar-tooltip").Heading("Tooltip").File("AvatarTooltipExample.cs").Demo(AvatarTooltipExample).Testid("ui-avatar-tooltip")
                .Blurb("Names it on hover and keyboard focus."),
            UiDocsExample.Key("ui-avatar-initials").Heading("Initials").File("AvatarInitialsExample.cs").Demo(AvatarInitialsExample).Testid("ui-avatar-initials")
                .Blurb("Two words give two first letters, one word its first two; InitialsSingle() keeps one, and "
                       + "Initials states them."),
            UiDocsExample.Key("ui-avatar-sizes").Heading("Sizes").File("AvatarSizesExample.cs").Demo(AvatarSizesExample).Testid("ui-avatar-sizes")
                .Blurb("Five sizes; 40px when it says none."),
            UiDocsExample.Key("ui-avatar-icon").Heading("Icon").File("AvatarIconExample.cs").Demo(AvatarIconExample).Testid("ui-avatar-icon")
                .Blurb("An icon in place of initials."),
            UiDocsExample.Key("ui-avatar-colors").Heading("Colors").File("AvatarColorsExample.cs").Demo(AvatarColorsExample).Testid("ui-avatar-colors")
                .Blurb("Any of Flux's seventeen hues behind the initials."),
            UiDocsExample.Key("ui-avatar-auto").Heading("Auto color").File("AvatarAutoColorExample.cs").Demo(AvatarAutoColorExample).Testid("ui-avatar-auto")
                .Blurb("ColorAuto() picks a hue from the initials, or from ColorSeed, so one person is one colour."),
            UiDocsExample.Key("ui-avatar-circle").Heading("Circle").File("AvatarCircleExample.cs").Demo(AvatarCircleExample).Testid("ui-avatar-circle"),
            UiDocsExample.Key("ui-avatar-badge").Heading("Badge").File("AvatarBadgeExample.cs").Demo(AvatarBadgeExample).Testid("ui-avatar-badge")
                .Blurb("A mark on a corner: a plain dot, a count, an emoji or anything else."),
            UiDocsExample.Key("ui-avatar-groups").Heading("Groups").File("AvatarGroupsExample.cs").Demo(AvatarGroupsExample).Testid("ui-avatar-groups")
                .Blurb("Stacked, each ringed in the colour of the page behind it."),
            UiDocsExample.Key("ui-avatar-as-button").Heading("As a button or a link").File("AvatarAsButtonExample.cs").Demo(AvatarAsButtonExample).Testid("ui-avatar-as-button"),
            UiAvatarBehaviour
        ];
}
