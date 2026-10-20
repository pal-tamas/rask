using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's profile as a C# component: each example with its own code, and its reference.</summary>
[Route("profile")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiProfilePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Profile.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Profile)
            .Lead("The signed-in person as a button: their avatar, their name if you give it, and a chevron that says "
                  + "it opens something. The menu it opens is a dropdown's.")
            .Api(UiApi.Profile)[
            UiDocsExample.Key("ui-profile").Heading("Profile").File("ProfileExample.cs").Demo(ProfileExample).Testid("ui-profile")
                .Blurb("An avatar and a chevron."),
            UiDocsExample.Key("ui-profile-name").Heading("With a name").File("ProfileNameExample.cs").Demo(ProfileNameExample).Testid("ui-profile-name"),
            UiDocsExample.Key("ui-profile-circle").Heading("Without a chevron, and round").File("ProfileCircleExample.cs").Demo(ProfileCircleExample).Testid("ui-profile-circle")
                .Blurb("Chevron(false) drops the chevron; Circle() rounds the avatar and the button with it."),
            UiDocsExample.Key("ui-profile-initials").Heading("Initials").File("ProfileInitialsExample.cs").Demo(ProfileInitialsExample).Testid("ui-profile-initials")
                .Blurb("No picture: the initials of Name, or of AvatarName for a profile that shows none, on any colour, "
                       + "or stated with Initials."),
            UiDocsExample.Key("ui-profile-icon").Heading("Trailing icon").File("ProfileTrailingIconExample.cs").Demo(ProfileTrailingIconExample).Testid("ui-profile-icon")
                .Blurb("Another icon in the chevron's place: up and down, for a profile switcher."),
            UiDocsExample.Key("ui-profile-menu").Heading("With a menu").File("ProfileMenuExample.cs").Demo(ProfileMenuExample).Testid("ui-profile-menu")
                .Blurb("A dropdown makes it the trigger of the account menu."),
            UiProfileBehaviour
        ];
}
