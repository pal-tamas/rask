namespace Rask.UiTests.Components;

/// <summary>
///     An avatar for a person who has no picture, which is most of them.
/// </summary>
/// <remarks>
///     <c>Src</c> used to be required, which made the component unusable for the case it is most often reached
///     for — a signed-in account. The monogram is the fallback, and the thing it must not lose on the way is
///     the NAME: "TP" read letter by letter tells a reader nothing.
/// </remarks>
public partial class UiAvatarNameTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_picture_is_drawn_when_there_is_one()
    {
        var html = Ui.Avatar.Src("/me.png").Alt("Ada Lovelace").ToHtml();

        Assert.Contains("src=\"/me.png\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Ada Lovelace\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_picture_it_draws_the_monogram()
    {
        var html = Ui.Avatar.Name("Ada Lovelace").ToHtml();

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("avatar-placeholder", html, StringComparison.Ordinal);
        Assert.Contains(">AL<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_monogram_is_hidden_and_the_name_is_what_is_announced()
    {
        // The letters are decoration standing in for a face. What a screen reader needs is the person.
        var html = Ui.Avatar.Name("Ada Lovelace").ToHtml();

        Assert.Contains("role=\"img\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Ada Lovelace\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picture_with_no_alt_falls_back_to_the_name() =>
        Assert.Contains("alt=\"Ada Lovelace\"",
            Ui.Avatar.Src("/me.png").Name("Ada Lovelace").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Size_and_rounding_are_the_same_rules_either_way()
    {
        // The placeholder is a <div> where the <img> would be, so it wears the same frame — an avatar that
        // changed shape the moment somebody removed their picture would be a visible glitch in a list.
        var picture = Ui.Avatar.Src("/me.png").Alt("Ada").Size(Ui.Size.Lg).Round(false).ToHtml();
        var monogram = Ui.Avatar.Name("Ada").Size(Ui.Size.Lg).Round(false).ToHtml();

        Assert.Contains("rounded\"", picture, StringComparison.Ordinal);
        Assert.Contains("rounded", monogram, StringComparison.Ordinal);
        Assert.DoesNotContain("rounded-full", monogram, StringComparison.Ordinal);
    }
}
