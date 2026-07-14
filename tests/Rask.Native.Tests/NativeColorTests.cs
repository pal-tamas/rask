using Rask.Native.Components;

namespace Rask.Native.Tests;

// NativeColor is the cross-platform color value type (the NativeIcon sibling). Its ToToken() is the wire
// form the chrome descriptor serializes and the platform heads parse, so these guard exactly that contract.
public class NativeColorTests
{
    [Theory]
    [InlineData("#abc", "#AABBCCFF")]     // #RGB → expanded + opaque + upper
    [InlineData("f00", "#FF0000FF")]      // no '#', shorthand
    [InlineData("#1234", "#11223344")]    // #RGBA shorthand
    [InlineData("#12ab56", "#12AB56FF")]  // #RRGGBB → opaque
    [InlineData("#12345678", "#12345678")] // #RRGGBBAA passthrough
    public void Hex_normalizes_to_RRGGBBAA(string input, string expected) =>
        Assert.Equal(expected, NativeColor.Hex(input).ToToken());

    [Theory]
    [InlineData("nope")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")] // 5 nibbles — no valid shape
    public void Hex_rejects_non_hex(string input) => Assert.Throws<FormatException>(() => NativeColor.Hex(input));

    [Fact]
    public void Rgba_formats_channels_opaque_by_default()
    {
        Assert.Equal("#FF0000FF", NativeColor.Rgba(255, 0, 0).ToToken());
        Assert.Equal("#01020304", NativeColor.Rgba(1, 2, 3, 4).ToToken());
    }

    [Fact]
    public void System_is_the_default_and_carries_no_token()
    {
        Assert.True(default(NativeColor).IsSystem);
        Assert.Null(NativeColor.System.ToToken());
        Assert.Equal(default, NativeColor.System);
    }

    [Fact]
    public void Curated_members_have_stable_tokens()
    {
        Assert.Equal("#FFFFFFFF", NativeColor.White.ToToken());
        Assert.Equal("#000000FF", NativeColor.Black.ToToken());
        Assert.Equal("#00000000", NativeColor.Clear.ToToken());
    }

    [Fact]
    public void Adaptive_pairs_light_and_dark()
    {
        var c = NativeColor.Adaptive(NativeColor.Hex("#fff"), NativeColor.Hex("#000"));
        Assert.Equal("#FFFFFFFF|#000000FF", c.ToToken());
    }

    [Fact]
    public void Adaptive_collapses_equal_sides_to_a_single_token()
    {
        var c = NativeColor.Adaptive(NativeColor.White, NativeColor.White);
        Assert.Equal("#FFFFFFFF", c.ToToken());
    }

    [Fact]
    public void Adaptive_reuses_the_other_side_when_one_is_System()
    {
        Assert.Equal("#000000FF", NativeColor.Adaptive(NativeColor.System, NativeColor.Black).ToToken());
        Assert.Equal("#FFFFFFFF", NativeColor.Adaptive(NativeColor.White, NativeColor.System).ToToken());
    }

    [Fact]
    public void Adaptive_of_two_System_sides_is_System() =>
        Assert.True(NativeColor.Adaptive(NativeColor.System, NativeColor.System).IsSystem);

    [Fact]
    public void Adaptive_takes_the_matching_half_of_adaptive_arguments()
    {
        var light = NativeColor.Adaptive(NativeColor.Hex("#111"), NativeColor.Hex("#222"));
        var dark = NativeColor.Adaptive(NativeColor.Hex("#333"), NativeColor.Hex("#444"));
        // light side → light's light half; dark side → dark's dark half.
        Assert.Equal("#111111FF|#444444FF", NativeColor.Adaptive(light, dark).ToToken());
    }

    [Fact]
    public void Equal_by_value_regardless_of_constructor()
    {
        Assert.Equal(NativeColor.Hex("#FF0000"), NativeColor.Rgba(255, 0, 0));
        Assert.NotEqual(NativeColor.Hex("#FF0000"), NativeColor.Hex("#00FF00"));
    }
}
