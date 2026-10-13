namespace Rask.UiTests.Components;

/// <summary>
///     Flux's flag: which file a region code draws, what the frame is, and what an unknown code does.
/// </summary>
public partial class UiFlagTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_country_is_a_lazy_image_of_its_file_in_a_three_by_two_frame()
    {
        var flag = Ui.Flag.Country("US");

        var html = flag.ToHtml();

        Assert.StartsWith("<span class=\"relative block shrink-0 overflow-hidden bg-zinc-100 ", html, StringComparison.Ordinal);
        Assert.Contains("[:where(&amp;)]:w-6 aspect-3/2 rounded-[2px]\" data-ui-flag data-country=\"US\">", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"\" src=\"/flags/US.svg?v=" + UiFlagRegistry.Version + "\" decoding=\"async\" loading=\"lazy\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("us", "US")]
    [InlineData("De", "DE")]
    [InlineData("XK", "XK")]
    [InlineData("CP", "FR")]
    [InlineData("DG", "IO")]
    [InlineData("EA", "ES")]
    public void A_code_is_read_in_either_case_and_three_share_another_countrys_flag(string country, string file)
    {
        var flag = Ui.Flag.Country(country);

        var html = flag.ToHtml();

        Assert.Contains("src=\"/flags/" + file + ".svg?", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("UN")]
    [InlineData("USA")]
    [InlineData("../x")]
    [InlineData("")]
    public void A_code_with_no_drawing_is_the_empty_frame_and_never_a_path(string country)
    {
        var flag = Ui.Flag.Country(country);

        var html = flag.ToHtml();

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.EndsWith("</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_code_in_the_registry_has_its_file_and_every_file_its_code()
    {
        var folder = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Flags");

        var files = Directory.GetFiles(folder, "*.svg").Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.Equal(254, files.Count);
        Assert.All(files, code => Assert.Equal(code, UiFlagRegistry.File(code!)));
        Assert.True(File.Exists(Path.Combine(folder, "LICENSE.txt")), "the drawings ship with their licence.");
    }

    [Fact]
    public void A_source_of_the_apps_own_wins_over_the_country()
    {
        var flag = Ui.Flag.Country("US").Src("/img/flags/california.svg").Alt("California");

        var html = flag.ToHtml();

        Assert.Contains("alt=\"California\" src=\"/img/flags/california.svg\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-country", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.FlagSize.Xs, "w-5")]
    [InlineData(Ui.FlagSize.Sm, "w-6")]
    [InlineData(Ui.FlagSize.Md, "w-8")]
    [InlineData(Ui.FlagSize.Lg, "w-10")]
    [InlineData(Ui.FlagSize.Xl, "w-12")]
    public void A_size_is_a_width_that_a_width_from_the_call_site_beats(Ui.FlagSize size, string width)
    {
        var flag = Ui.Flag.Country("US").Size(size).Class("w-7");

        var html = flag.ToHtml();

        Assert.Contains("[:where(&amp;)]:" + width + " aspect-3/2 rounded-[2px] w-7\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_circle_is_the_same_flag_cropped_square_and_round()
    {
        var flag = Ui.Flag.Country("JP").Circle();

        var html = flag.ToHtml();

        Assert.Contains("aspect-square rounded-full", html, StringComparison.Ordinal);
        Assert.Contains("class=\"size-full object-cover\"", html, StringComparison.Ordinal);
    }
}
