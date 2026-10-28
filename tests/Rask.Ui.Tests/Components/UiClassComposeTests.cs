namespace Rask.UiTests.Components;

// Every kit component joins its class list with UiClass.Compose: one space between parts, each trimmed, blanks left
// out, and a lone part handed back as itself rather than copied.
public class UiClassComposeTests
{
    [Fact]
    public void A_lone_part_comes_back_as_the_same_string()
    {
        var part = string.Concat("rounded ", "p-2");

        var joined = UiClass.Compose(null, part, " ");

        Assert.Same(part, joined);
    }

    [Fact]
    public void Parts_are_trimmed_and_joined_by_one_space_with_blanks_left_out()
    {
        string?[] parts = [" a ", null, "", "b", "  ", "c  "];

        var joined = UiClass.Compose(parts);

        Assert.Equal("a b c", joined);
    }

    [Fact]
    public void A_lone_part_with_spaces_around_it_comes_back_trimmed()
    {
        const string part = "  a  ";

        var joined = UiClass.Compose(part, null);

        Assert.Equal("a", joined);
    }

    [Fact]
    public void Nothing_but_blanks_joins_to_the_empty_string()
    {
        string?[] parts = [null, " ", ""];

        var joined = UiClass.Compose(parts);

        Assert.Equal(string.Empty, joined);
    }
}
