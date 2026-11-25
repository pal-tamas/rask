// These tests prove what a call given no token does, so they pass none — as a strict app's test project, where
// xUnit1051 is off, writes them.
#pragma warning disable xUnit1051
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// Rask.Testing's xUnit v3 hook makes the test's cancellation the ambient one, so no Rask call in a test is handed a token.
public sealed class TestCancellationTests
{
    [Fact]
    public void Inside_a_test_the_work_in_progress_is_cancelled_with_the_test()
    {
        var test = TestContext.Current.CancellationToken;

        var ambient = Current.Cancellation;

        Assert.True(ambient.CanBeCanceled);
        Assert.Equal(test, ambient);
    }

    [Fact]
    public void A_token_passed_by_hand_still_wins_over_the_tests()
    {
        using var own = new CancellationTokenSource();

        var used = Ambient.Or(own.Token);

        Assert.Equal(own.Token, used);
    }

    [Fact]
    public void A_render_that_sets_the_work_aside_does_not_get_the_tests_cancellation()
    {
        var test = TestContext.Current.CancellationToken;

        CancellationToken inside;
        using (Ambient.Enter(CancellationToken.None))
        {
            inside = Ambient.CancellationToken;
        }

        Assert.NotEqual(test, inside);
        Assert.Equal(test, Ambient.CancellationToken);
    }

    [Fact]
    public async Task A_database_started_with_no_token_is_cancelled_with_the_work_in_progress()
    {
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        using var work = Ambient.Enter(stopped.Token);
        var starting = TestDatabase.Start<PumpsContext>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
    }

    [Fact]
    public async Task A_database_started_and_read_with_no_token_runs_under_the_tests_cancellation()
    {
        await using var database = await TestDatabase.Start<PumpsContext>();
        var company = Company.Named("Acme");
        database.Context.Add(company);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var count = await Company.Count();
        var loaded = await database.Load<Company>(company.Id);

        Assert.Equal(1, count);
        Assert.Equal("Acme", loaded!.Name);
    }
}
