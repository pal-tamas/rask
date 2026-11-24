using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Data.Tests;

// A generated form model borrows the rules its aggregate states in its own static Rules: a form over HarbourModel asks
// Harbour's rules of the members that carry them, each row's own rules under its place, and is told their hints.
public sealed class FluentModelFormTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_generated_model_is_checked_by_its_aggregates_rules()
    {
        var model = new HarbourModel { Name = "", Email = "nowhere", Code = "H", Region = "North" };

        var result = await Validator<HarbourModel>().ValidateAsync(new ValidationContext<object>(model), Token);

        Assert.Equal(["Email", "Name"], result.Errors.Select(static e => e.PropertyName).Order(StringComparer.Ordinal));
        Assert.Contains(result.Errors, static e => e.ErrorMessage == "Not an address.");
    }

    [Fact]
    public async Task A_rows_rules_are_asked_under_its_place_in_the_list()
    {
        var model = new HarbourModel { Name = "Bergen", Code = "H", Region = "North" };
        model.Quays.Add(new QuayModel { Length = 10 });
        model.Quays.Add(new QuayModel { Length = 0 });

        var result = await Validator<HarbourModel>().ValidateAsync(new ValidationContext<object>(model), Token);

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Quays[1].Length", failure.PropertyName);
        Assert.Equal("A quay has a length.", failure.ErrorMessage);
    }

    [Fact]
    public async Task An_awaited_rule_of_the_aggregate_runs_on_the_form()
    {
        var model = new HarbourModel { Name = "Bergen", Code = "TAKEN", Region = "North" };

        var result = await Validator<HarbourModel>().ValidateAsync(new ValidationContext<object>(model), Token);

        Assert.Equal("That code is taken.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void The_model_is_told_the_hints_its_aggregates_rules_give()
    {
        var name = StatedRules.FacetsOf(typeof(HarbourModel), "Name");

        var code = StatedRules.FacetsOf(typeof(HarbourModel), "Code");

        Assert.Equal(new StatedFacets(Required: true, MaxLength: 20), name);
        Assert.True(code.Required);
        Assert.True(RaskValidators.BorrowsRules(typeof(HarbourModel)));
    }

    [Fact]
    public void A_model_whose_aggregate_states_nothing_gets_no_validator()
    {
        var found = RaskValidators.Find(typeof(VoyageModel));

        var borrows = RaskValidators.BorrowsRules(typeof(VoyageModel));

        Assert.Null(found);
        Assert.False(borrows);
    }

    private static IValidator Validator<T>() => (IValidator)RaskValidators.Find(typeof(T))!(null);
}
