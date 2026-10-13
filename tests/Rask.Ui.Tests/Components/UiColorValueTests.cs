namespace Rask.UiTests.Components;

/// <summary>
///     The color picker's colour maths, held to what Flux's picker wrote for the same text on fluxui.dev:
///     typed into its first example, with each <c>format</c> written on the live element in turn.
/// </summary>
public class UiColorValueTests
{
    [Theory]
    [InlineData("#bada55", "#bada55")]
    [InlineData("#BADA5580", "#bada55")]
    [InlineData("#abc", "#aabbcc")]
    [InlineData("#f008", "#ff0000")]
    [InlineData("bada55", "#bada55")]
    [InlineData("abc", "#aabbcc")]
    [InlineData("ff000080", "#ff0000")]
    [InlineData(" #abcdef ", "#abcdef")]
    [InlineData("rgb(1,2,3)", "#010203")]
    [InlineData("rgba(1,2,3,.5)", "#010203")]
    [InlineData("rgb(300 -5 12.6)", "#ff000d")]
    [InlineData("rgb(50% 20% 10%)", "#80331a")]
    [InlineData("rgb(12.5 12.4 0.5)", "#0d0c01")]
    [InlineData("rgb(none 2 3)", "#000203")]
    [InlineData("hsl(120 50% 50%)", "#40bf40")]
    [InlineData("hsl(120deg 50% 50%)", "#40bf40")]
    [InlineData("hsla(120, 50%, 50%, 0.5)", "#40bf40")]
    [InlineData("hsl(0.5turn 100% 50%)", "#00ffff")]
    [InlineData("hsl(74 64% 59%)", "#bad954")]
    [InlineData("hsl(359.6 33.3% 66.6%)", "#c68d8e")]
    [InlineData("hwb(120 10% 20%)", "#1acc1a")]
    [InlineData("hwb(120 60% 60%)", "#808080")]
    [InlineData("rebeccapurple", "#663399")]
    [InlineData("RED", "#ff0000")]
    [InlineData("transparent", "#000000")]
    public void A_colour_of_the_old_syntaxes_is_written_as_the_hex_flux_writes(string typed, string written)
    {
        var read = UiColorValue.TryParse(typed, out var color);

        var hex = color.Format(Ui.ColorPickerFormat.Hex);

        Assert.True(read);
        Assert.Equal(written, hex);
    }

    // Flux reads the numbers of the browser's computed colour as red, green and blue, whatever they measure:
    // every row is what its picker showed for the text.
    [Theory]
    [InlineData("oklch(0.7 0.1 200)", "#0100c8")]
    [InlineData("oklch(70% 0.1 200)", "#0100c8")]
    [InlineData("oklch(0.7 0.1 200deg)", "#0100c8")]
    [InlineData("oklch(0.7 0.1 200 / 0.5)", "#0100c8")]
    [InlineData("oklch(0.7 0.1 0.5turn)", "#0100b4")]
    [InlineData("oklch(0.7 40% 200)", "#0100c8")]
    [InlineData("oklch(1.5 0.1 200)", "#0100c8")]
    [InlineData("oklch(0.7 -0.1 200)", "#0100c8")]
    [InlineData("oklch(0.7 0.1 -30)", "#010014a")]
    [InlineData("oklch(0.7 0.1 400)", "#010028")]
    [InlineData("oklch(0.7 none 200)", "#NaNNaNNaN")]
    [InlineData("oklch(none 0.1 200)", "#NaNNaNNaN")]
    [InlineData("oklch(0.123456789 0.3 29.9999)", "#00001e")]
    [InlineData("oklch(0.999999 0.00001 120)", "#010078")]
    [InlineData("oklch(0.5 0.0000001 120)", "#010107")]
    [InlineData("oklch(0.7 0.1 3.14159rad)", "#0100b4")]
    [InlineData("oklch(0.7 0.1 100grad)", "#01005a")]
    [InlineData("OKLCH(0.7 0.1 200)", "#0100c8")]
    [InlineData("oklch(62.5% 0.25 29)", "#01001d")]
    [InlineData("oklab(0.7 0.1 -0.1)", "#010000")]
    [InlineData("oklab(0.7 50% -50%)", "#010000")]
    [InlineData("oklab(2 1 1)", "#010101")]
    [InlineData("lab(50 40 30)", "#32281e")]
    [InlineData("lab(50% 40 30)", "#32281e")]
    [InlineData("lab(50 -40 30)", "#32281e")]
    [InlineData("lab(50 40% -30%)", "#323226")]
    [InlineData("lab(150 40 30)", "#64281e")]
    [InlineData("lab(29.2345 39.3825 20.0664)", "#1d2714")]
    [InlineData("lab(-10 200 -200)", "#00c8c8")]
    [InlineData("lch(50 40 30)", "#32281e")]
    [InlineData("lch(50% 40 300)", "#322812c")]
    [InlineData("lch(50 40 -30)", "#322814a")]
    [InlineData("lch(50 40 0.5turn)", "#3228b4")]
    [InlineData("lch(50 150% 30)", "#32e11e")]
    [InlineData("lch(50 -5 30)", "#32001e")]
    [InlineData("lch(52.2345 72.2 56.2)", "#344838")]
    [InlineData("color(srgb 1 0 0)", "#010000")]
    [InlineData("color(srgb 0.5 0.25 1)", "#000001")]
    [InlineData("color(display-p3 1 0 0)", "#030100")]
    [InlineData("color(srgb 100% 50% 0%)", "#010100")]
    [InlineData("color(xyz 0.3 0.4 0.5)", "#410000")]
    [InlineData("color(xyz-d50 0.3 0.4 0.5)", "#320000")]
    [InlineData("color(xyz-d65 0.3 0.4 0.5)", "#410000")]
    [InlineData("color(srgb-linear 0.5 0.5 0.5)", "#010101")]
    [InlineData("color(rec2020 1 0.5 0)", "#7e40100")]
    [InlineData("color(a98-rgb 0.2 0.4 0.6)", "#620000")]
    [InlineData("color(prophoto-rgb 0.2 0.4 0.6)", "#000001")]
    [InlineData("color(srgb 2 -1 0.5)", "#020101")]
    [InlineData("color(srgb 0.3333333 0.6666666 0.123456789)", "#000100")]
    [InlineData("color(display-p3 0.5 none 1)", "#030001")]
    public void A_wide_gamut_colour_becomes_exactly_what_flux_makes_of_it(string typed, string written)
    {
        var read = UiColorValue.TryParse(typed, out var color);

        var hex = color.Format(Ui.ColorPickerFormat.Hex);

        Assert.True(read);
        Assert.Equal(written, hex);
    }

    // Channels on a half: which way each rounds is the last bit of Flux's own arithmetic.
    [Theory]
    [InlineData("color(srgb 2.5 15.5 188.5)", "#020fbd")]
    [InlineData("color(srgb 187 157.5 169)", "#bb9ea9")]
    [InlineData("color(srgb 186 12.5 98.5)", "#ba0c62")]
    [InlineData("color(srgb 241.5 233.5 94.4)", "#f2ea5e")]
    [InlineData("color(srgb 48 232.5 142)", "#30e98e")]
    [InlineData("color(srgb 132 73.5 253.5)", "#8449fd")]
    [InlineData("color(srgb 104.5 7.5 186.9)", "#6807bb")]
    [InlineData("color(srgb 103 156.5 31.5)", "#679d20")]
    [InlineData("color(srgb 38.5 213.5 76)", "#27d64c")]
    [InlineData("color(srgb 56.5 182.5 172)", "#39b6ac")]
    [InlineData("color(srgb 140.5 123.5 89)", "#8d7c59")]
    [InlineData("color(srgb 186.5 38.5 127.5)", "#ba2680")]
    [InlineData("color(srgb 43.5 94.5 224)", "#2c5fe0")]
    [InlineData("color(srgb 115 238.5 41.5)", "#73ef29")]
    [InlineData("color(srgb 111 40.5 104.5)", "#6f2969")]
    [InlineData("color(srgb 70.5 76 10.5)", "#474c0b")]
    [InlineData("color(srgb 108.5 42.5 124.5)", "#6c2b7d")]
    [InlineData("color(srgb 13.5 192.5 204.5)", "#0ec0cd")]
    public void A_channel_on_a_half_rounds_the_way_fluxs_arithmetic_rounds_it(string typed, string written)
    {
        UiColorValue.TryParse(typed, out var color);

        var hex = color.Format(Ui.ColorPickerFormat.Hex);

        Assert.Equal(written, hex);
    }

    [Theory]
    [InlineData("#bada55", "#bada55ff")]
    [InlineData("#ff000080", "#ff000080")]
    [InlineData("rgb(1 2 3 / 0.125)", "#01020320")]
    [InlineData("transparent", "#00000000")]
    [InlineData("rgb(200 100 50 / 0.999)", "#c86432ff")]
    [InlineData("rgb(200 100 50 / 0.001)", "#c8643200")]
    [InlineData("oklch(0.7 0.1 -30)", "#010014aff")]
    [InlineData("oklch(0.7 none 200)", "#NaNNaNNaNff")]
    public void Hexa_carries_the_alpha_as_two_more_digits(string typed, string written)
    {
        UiColorValue.TryParse(typed, out var color);

        var hexa = color.Format(Ui.ColorPickerFormat.Hexa);

        Assert.Equal(written, hexa);
    }

    [Theory]
    [InlineData("#bada55", Ui.ColorPickerFormat.Rgb, "rgb(186 218 85)")]
    [InlineData("#ff000080", Ui.ColorPickerFormat.Rgb, "rgb(255 0 0)")]
    [InlineData("oklch(0.7 0.1 -30)", Ui.ColorPickerFormat.Rgb, "rgb(1 0 330)")]
    [InlineData("oklch(0.7 none 200)", Ui.ColorPickerFormat.Rgb, "rgb(NaN NaN NaN)")]
    [InlineData("#bada55", Ui.ColorPickerFormat.Rgba, "rgb(186 218 85 / 1)")]
    [InlineData("#ff000080", Ui.ColorPickerFormat.Rgba, "rgb(255 0 0 / 0.5)")]
    [InlineData("#f008", Ui.ColorPickerFormat.Rgba, "rgb(255 0 0 / 0.53)")]
    [InlineData("rgb(1 2 3 / 0.125)", Ui.ColorPickerFormat.Rgba, "rgb(1 2 3 / 0.13)")]
    [InlineData("rgb(1 2 3 / 0.333)", Ui.ColorPickerFormat.Rgba, "rgb(1 2 3 / 0.33)")]
    [InlineData("rgb(1 2 3 / 0.005)", Ui.ColorPickerFormat.Rgba, "rgb(1 2 3 / 0)")]
    [InlineData("rgb(1 2 3 / 0.995)", Ui.ColorPickerFormat.Rgba, "rgb(1 2 3 / 1)")]
    [InlineData("oklch(0.7 0.1 200 / 37%)", Ui.ColorPickerFormat.Rgba, "rgb(1 0 200 / 0.37)")]
    [InlineData("color(display-p3 1 0 0)", Ui.ColorPickerFormat.Rgba, "rgb(3 1 0 / 0)")]
    [InlineData("color(xyz 0.3 0.4 0.5)", Ui.ColorPickerFormat.Rgba, "rgb(65 0 0 / 0.5)")]
    [InlineData("color(rec2020 1 0.5 0)", Ui.ColorPickerFormat.Rgba, "rgb(2020 1 0 / 0)")]
    [InlineData("lab(50 40 30 / 50%)", Ui.ColorPickerFormat.Rgba, "rgb(50 40 30 / 0.5)")]
    [InlineData("transparent", Ui.ColorPickerFormat.Rgba, "rgb(0 0 0 / 0)")]
    [InlineData("#bada55", Ui.ColorPickerFormat.Hsl, "hsl(74 64% 59%)")]
    [InlineData("rgb(1 2 3 / 0.125)", Ui.ColorPickerFormat.Hsl, "hsl(210 50% 1%)")]
    [InlineData("#808080", Ui.ColorPickerFormat.Hsl, "hsl(0 0% 50%)")]
    [InlineData("#7f7f7f", Ui.ColorPickerFormat.Hsl, "hsl(0 0% 50%)")]
    [InlineData("#123456", Ui.ColorPickerFormat.Hsl, "hsl(210 65% 20%)")]
    [InlineData("rgb(254 1 1)", Ui.ColorPickerFormat.Hsl, "hsl(0 99% 50%)")]
    [InlineData("#0ff1ce", Ui.ColorPickerFormat.Hsl, "hsl(171 89% 50%)")]
    [InlineData("hsl(359.6 33.3% 66.6%)", Ui.ColorPickerFormat.Hsl, "hsl(359 33% 66%)")]
    [InlineData("rgb(200 100 50 / 0.999)", Ui.ColorPickerFormat.Hsl, "hsl(20 60% 49%)")]
    [InlineData("oklch(0.7 0.1 -30)", Ui.ColorPickerFormat.Hsl, "hsl(240 183% 65%)")]
    [InlineData("oklch(0.5 0.0000001 120)", Ui.ColorPickerFormat.Hsl, "hsl(240 75% 2%)")]
    [InlineData("color(srgb 0.5 0.25 1)", Ui.ColorPickerFormat.Hsl, "hsl(240 100% 0%)")]
    [InlineData("oklch(0.7 none 200)", Ui.ColorPickerFormat.Hsl, "hsl(NaN NaN% NaN%)")]
    [InlineData("#ff000080", Ui.ColorPickerFormat.Hsla, "hsl(0 100% 50% / 0.5)")]
    [InlineData("rgb(10 20 30 / 0.5)", Ui.ColorPickerFormat.Hsla, "hsl(210 50% 8% / 0.5)")]
    [InlineData("#bada55", Ui.ColorPickerFormat.Hsla, "hsl(74 64% 59% / 1)")]
    public void Rgb_and_hsl_are_written_in_the_space_syntax_with_whole_numbers(string typed, Ui.ColorPickerFormat format, string written)
    {
        UiColorValue.TryParse(typed, out var color);

        var text = color.Format(format);

        Assert.Equal(written, text);
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("12")]
    [InlineData("f008")]
    [InlineData("zzz")]
    [InlineData("# abc")]
    [InlineData("0")]
    [InlineData("255 0 0")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("rgb(1 2)")]
    [InlineData("oklch(0.7, 0.1, 200)")]
    [InlineData("inherit")]
    [InlineData("calc(1)")]
    [InlineData("light-dark(red, blue)")]
    public void Text_that_is_no_colour_is_refused(string? typed)
    {
        var read = UiColorValue.TryParse(typed, out var color);

        Assert.False(read);
        Assert.Equal(UiColorValue.Black, color);
    }

    [Fact]
    public void A_colour_is_held_as_hue_saturation_and_brightness()
    {
        UiColorValue.TryParse("#bada55", out var color);

        var (hue, saturation, brightness) = (color.Hue, color.Saturation, color.Brightness);

        // Flux's thumb stands at 61.0092%, 14.5098% for this colour, and its hue slider at 74.
        Assert.Equal(74.4361, hue, 4);
        Assert.Equal(61.0092, saturation, 4);
        Assert.Equal(100 - 14.5098, brightness, 4);
    }

    [Fact]
    public void Black_keeps_the_hue_it_was_reached_from()
    {
        UiColorValue.TryParse("#bada55", out var color);

        var black = color with { Brightness = 0 };

        Assert.Equal("#000000", black.Hex);
        Assert.Equal(color.Hue, black.Hue);
        Assert.Equal("rgb(194 255 0)", black.HueCss);
    }

    [Fact]
    public void The_painted_colour_carries_its_alpha_only_when_it_has_one()
    {
        UiColorValue.TryParse("rgb(99 102 241 / 0.5)", out var half);

        var (painted, opaque) = (half.Css, half.OpaqueCss);

        Assert.Equal("rgb(99 102 241 / 0.5)", painted);
        Assert.Equal("rgb(99 102 241)", opaque);
    }
}
