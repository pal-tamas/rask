using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's OTP input and its parts as C# components: each example with its own code, and its reference.</summary>
[Route("otp-input")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiOtpPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Otp.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Otp)
            .Lead("A one-time password typed a character to a cell, and the code they spell bound as one string. A "
                  + "character moves on to the next cell, Backspace walks back, and a pasted code fills every cell.")
            .Api(UiApi.Otp)[
            UiDocsExample.Key("ui-otp").Heading("OTP input").File("OtpExample.cs").Demo(OtpExample).Testid("ui-otp"),
            UiDocsExample.Key("ui-otp-form").Heading("Example usage").File("OtpFormExample.cs").Demo(OtpFormExample).Testid("ui-otp-form")
                .Blurb("A form for verifying a one-time password: the page empties the code to resend one, and the cells follow."),
            UiDocsExample.Key("ui-otp-auto").Heading("Autosubmit").File("OtpAutoSubmitExample.cs").Demo(OtpAutoSubmitExample).Testid("ui-otp-auto")
                .Blurb("OnComplete runs as soon as the last cell is filled: verify, submit or move on from it."),
            UiDocsExample.Key("ui-otp-alphanumeric").Heading("Alphanumeric").File("OtpAlphanumericExample.cs").Demo(OtpAlphanumericExample).Testid("ui-otp-alphanumeric")
                .Blurb("Letters as well as digits, upper-cased in the value. Alpha takes letters alone."),
            UiDocsExample.Key("ui-otp-private").Heading("Private").File("OtpPrivateExample.cs").Demo(OtpPrivateExample).Testid("ui-otp-private")
                .Blurb("Masks each character, as a password field does."),
            UiDocsExample.Key("ui-otp-separator").Heading("Separator").File("OtpSeparatorExample.cs").Demo(OtpSeparatorExample).Testid("ui-otp-separator")
                .Blurb("Place the cells yourself to put a separator between them; they are counted, and Length is not needed."),
            UiDocsExample.Key("ui-otp-group").Heading("Group").File("OtpGroupExample.cs").Demo(OtpGroupExample).Testid("ui-otp-group")
                .Blurb("A group joins its cells into one box."),
            UiDocsExample.Key("ui-otp-groups").Heading("Group separator").File("OtpGroupSeparatorExample.cs").Demo(OtpGroupSeparatorExample).Testid("ui-otp-groups"),
            UiOtpBehaviour
        ];
}
