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

    [Fact]
    public async Task An_operation_input_read_with_Of_is_filled_from_the_stored_aggregate_at_its_version()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var memo = await Test.Make<Memo>();
        await Memo.Amend(memo.Id, memo.Version, "Diesel", 0m, Ct);

        var input = await MemoAmendInput.Of(memo.Id, Ct);

        Assert.Equal(memo.Id, input.Id);
        Assert.Equal(memo.Version + 1, input.Version);
        Assert.Equal("Diesel", input.Title);
        Assert.Equal(0m, input.Litres);
    }

    [Fact]
    public async Task A_refusal_that_reads_the_stored_aggregate_is_shown_on_a_form_started_with_Of()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var memo = await Test.Make<Memo>();

        var shows = Test.FormShows(await MemoAmendInput.Of(memo.Id, Ct), m => m.Litres, memo.Litres + 1, Memo.CannotGrow);

        await shows;
        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Of_an_id_nothing_is_stored_under_says_so()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var id = Guid.CreateVersion7();

        var read = MemoAmendInput.Of(id, Ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Contains("MemoAmendInput.Of(id)", error.Message, StringComparison.Ordinal);
    }
}
