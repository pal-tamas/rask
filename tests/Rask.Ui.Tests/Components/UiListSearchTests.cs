namespace Rask.UiTests.Components;

public class UiListSearchTests
{
    [Fact]
    public void A_prefix_typed_with_no_cursor_yet_finds_the_first_row_that_starts_so()
    {
        string?[] rows = ["Bern", "Oslo", "Berlin"];

        var found = UiListSearch.TypeAhead("ber", "r", -1, rows);

        Assert.Equal(0, found);
    }

    [Fact]
    public void One_letter_with_no_cursor_yet_finds_the_first_row_too()
    {
        string?[] rows = ["Bern", "Oslo", "Berlin"];

        var found = UiListSearch.TypeAhead("b", "b", -1, rows);

        Assert.Equal(0, found);
    }
}
