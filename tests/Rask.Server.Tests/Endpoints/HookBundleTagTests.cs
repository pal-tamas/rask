using Rask.Server.Http;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     The behaviour hooks' <c>&lt;script&gt;</c> goes into a first response only when the page asks for a hook.
/// </summary>
public sealed class HookBundleTagTests
{
    private const string Url = "/rask/rask-hooks.js?v=0123456789abcdef";
    private const string Runtime = "<script src=\"/rask/rask.js?v=0123456789abcdef\"></script>";

    [Fact]
    public void A_page_with_no_hooked_attribute_is_sent_as_it_was_rendered()
    {
        var html = $"<html><head></head><body><button data-rask-on-click=\"h0\" data-rask-focus-trap>go</button>{Runtime}</body></html>";

        var sent = HookBundleTag.AddTo(html, string.Empty, Url);

        Assert.Same(html, sent);
    }

    [Theory]
    [InlineData("<span data-rask-tooltip=\"tip\"></span>")]
    [InlineData("<dialog data-rask-modal-open=\"true\"></dialog>")]
    [InlineData("<div popover id=\"menu\"></div>")]
    [InlineData("<input type=\"checkbox\" role=\"switch\">")]
    [InlineData("<div role=\"listbox\" aria-activedescendant=\"o1\"></div>")]
    public void A_page_that_asks_for_a_hook_gets_the_bundle_after_the_runtime(string hooked)
    {
        var html = $"<html><head></head><body>{hooked}{Runtime}</body></html>";

        var sent = HookBundleTag.AddTo(html, string.Empty, Url);

        Assert.Equal(
            $"<html><head></head><body>{hooked}{Runtime}<script src=\"{Url}\" data-rask-hooks data-rask-managed></script></body></html>",
            sent);
    }

    [Fact]
    public void A_role_that_is_not_a_switch_and_a_longer_name_that_starts_like_a_hooks_ask_for_nothing()
    {
        var html = $"<html><head></head><body><p role=\"status\" data-rask-focus-trap data-rask-popover-open=\"true\">(data-rask-tooltip)</p>{Runtime}</body></html>";

        var sent = HookBundleTag.AddTo(html, string.Empty, Url);

        Assert.Same(html, sent);
    }

    [Fact]
    public void The_tag_goes_before_the_last_body_end_tag_when_a_script_spells_one_out()
    {
        var html = $"<html><body><div data-rask-otp></div><script>var s = \"</body>\";</script>{Runtime}</body></html>";

        var sent = HookBundleTag.AddTo(html, string.Empty, Url);

        Assert.EndsWith($"{Runtime}<script src=\"{Url}\" data-rask-hooks data-rask-managed></script></body></html>", sent, StringComparison.Ordinal);
    }
}
