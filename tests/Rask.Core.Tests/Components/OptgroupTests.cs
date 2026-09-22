namespace Rask.Core.Tests.Components;

public partial class OptgroupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<optgroup></optgroup>", Optgroup.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<optgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled label=\"Group\"></optgroup>",
            Optgroup
                .Disabled(true)
                .Label("Group")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<optgroup>&lt;x&gt;</optgroup>", Optgroup["<x>"].ToHtml());
}
