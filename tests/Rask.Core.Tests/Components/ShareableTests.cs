using Rask.Core.Browser;

namespace Rask.Core.Tests.Components;

// Shareable is headless: it renders whatever the Template returns and hands it the data-rask-share bundle
// (to spread onto the element's Data prop). The shared client handles the click locally (navigator.share in
// the gesture, or the native bridge). No IJSRuntime, no host-specific registration — the same trigger works
// on every host, Server included.
public class ShareableTests
{
    [Fact]
    public void Render_AppliesDataRaskShareToTemplateElement_SerializingOnlySetFields()
    {
        Assert.Equal(
            "<button data-rask-share=\"{&quot;title&quot;:&quot;Rask&quot;,&quot;url&quot;:&quot;https://x&quot;}\" type=\"button\">Share</button>",
            Shareable(
                new ShareData { Title = "Rask", Url = "https://x" },
                share => Button(Type: "button", Data: share)["Share"]).ToHtml());
    }

    [Fact]
    public void Render_WorksWithAnyElement_NotJustAButton()
    {
        // Headless: attach the share behaviour to a link (or any element with a Data prop).
        Assert.Equal(
            "<a data-rask-share=\"{&quot;text&quot;:&quot;hi&quot;}\" href=\"#\">Share</a>",
            Shareable(
                new ShareData { Text = "hi" },
                share => A(Href: "#", Data: share)["Share"]).ToHtml());
    }

    [Fact]
    public void Render_TemplateControlsAllMarkupAndEncodesChildText()
    {
        Assert.Equal(
            "<button data-rask-share=\"{&quot;title&quot;:&quot;t&quot;}\" type=\"button\">&lt;go&gt;</button>",
            Shareable(
                new ShareData { Title = "t" },
                share => Button(Type: "button", Data: share)["<go>"]).ToHtml());
    }
}
