using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>Test.FormShows over a factory's generated form: what the domain refuses is what the form shows.</summary>
public sealed class FormShowsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_refusal_in_the_factory_is_shown_under_its_field()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var shows = Test.FormShows<MemoWriteInput>(m => m.Title, "", Memo.NeedsATitle);

        await shows;
        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_refusal_of_a_row_is_shown_under_that_row()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var shows = Test.FormShows<MemoWriteInput>(m => m.Lines[0].Quantity, 150m, given: m => m.Title = "Diesel", Memo.TooMuch);

        await shows;
        Assert.True(shows.IsCompletedSuccessfully);
    }
}
