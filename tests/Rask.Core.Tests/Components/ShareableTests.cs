using Rask.Core.Browser;

namespace Rask.Core.Tests.Components;

// Shareable is headless: it renders whatever the Template returns and hands it the data-rask-share bundle
// (to spread onto the element's Data prop). The shared client handles the click locally (navigator.share in
// the gesture). No IJSRuntime, no host-specific registration — the same trigger works on every host,
// Server included.
public partial class ShareableTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_share_bundle_lands_on_the_template_element_with_only_the_set_fields()
    {
        Assert.Equal(
            "<button data-rask-share=\"{&quot;title&quot;:&quot;Rask&quot;,&quot;url&quot;:&quot;https://x&quot;}\" type=\"button\">Share</button>",
            Shareable
                .Data(new ShareData { Title = "Rask", Url = "https://x" })
                .Template(share => Button.Type("button").Data(share)["Share"]).ToHtml());
    }

    [Fact]
    public void Any_element_can_carry_the_share_bundle_not_just_a_button()
    {
        // Headless: attach the share behaviour to a link (or any element with a Data prop).
        Assert.Equal(
            "<a data-rask-share=\"{&quot;text&quot;:&quot;hi&quot;}\" href=\"#\">Share</a>",
            Shareable
                .Data(new ShareData { Text = "hi" })
                .Template(share => A.Href("#").Data(share)["Share"]).ToHtml());
    }

    [Fact]
    public void The_template_controls_all_markup_and_child_text_is_encoded()
    {
        Assert.Equal(
            "<button data-rask-share=\"{&quot;title&quot;:&quot;t&quot;}\" type=\"button\">&lt;go&gt;</button>",
            Shareable
                .Data(new ShareData { Title = "t" })
                .Template(share => Button.Type("button").Data(share)["<go>"]).ToHtml());
    }
}
