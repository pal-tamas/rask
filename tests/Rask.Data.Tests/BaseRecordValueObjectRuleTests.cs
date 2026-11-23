namespace Rask.Data.Tests;

/// <summary>
/// A value object declared from a Rask.Data base record checks its rules as it is made: what each base refuses, the
/// field a refusal is shown under, and the generated Create, TryCreate and CreateOptional.
/// </summary>
public sealed class BaseRecordValueObjectRuleTests
{
    [Fact]
    public void A_text_is_trimmed_unless_it_says_otherwise()
    {
        var driver = RigDriver.Create("  Ada  ");
        var chip = RigChip.Create(" C1 ");

        Assert.Equal("Ada", driver.Value);
        Assert.Equal(" C1 ", chip.Value);
        Assert.Equal("Ada", driver.ToString());
    }

    [Fact]
    public void Create_refuses_a_one_value_object_exactly_under_the_field_it_is_given()
    {
        var tooLong = Assert.Throws<RaskValidationException>(() => RigDriver.Create(new string('x', 21), "Driver"));
        var blank = Assert.Throws<RaskValidationException>(() => RigDriver.Create("   ", "Driver"));
        var unnamed = Assert.Throws<RaskValidationException>(() => RigAxles.Create(1));

        Assert.Equal([("Driver", "Enter at most 20 characters.")], Failures(tooLong));
        Assert.Equal([("Driver", GeneratedModelRules.RequiredMessage)], Failures(blank));
        Assert.Equal([("", "Enter 2 or more.")], Failures(unnamed));
    }

    [Fact]
    public void Each_one_value_base_says_which_rule_was_broken()
    {
        var today = Clock.Today;

        Assert.Equal([RigPlate.Form], RigPlate.__Problems("abc"));
        Assert.Equal(["Enter 1 or more.", "Enter at most 2 decimal places."], RigLitres.__Problems(0.125m));
        Assert.Equal(["The number is out of range."], RigLitres.__Problems(10_000_000m));
        Assert.Equal(["Enter 9 or less."], RigAxles.__Problems(10));
        Assert.Equal([GeneratedModelRules.RequiredMessage], RigAxles.__Problems(null));
        Assert.Single(RigInspected.__Problems(today.AddDays(1)));
        Assert.Empty(RigInspected.__Problems(today));
        Assert.Empty(RigChip.__Problems(" 123456789A "));
        Assert.Single(RigChip.__Problems(" 123456789AB "));
    }

    [Fact]
    public void A_moment_that_may_not_be_after_now_refuses_one_after_the_wall_clock()
    {
        var now = Clock.Wall;

        var later = RigFueled.__Problems(now.AddHours(1));
        var earlier = RigFueled.__Problems(now.AddMinutes(-1));

        Assert.Single(later);
        Assert.Empty(earlier);
    }

    [Fact]
    public void A_utc_moment_is_kept_on_the_wall_clock_of_the_apps_zone()
    {
        var utc = new DateTime(2024, 3, 31, 0, 30, 0, DateTimeKind.Utc);

        var fueled = RigFueled.Create(utc);

        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(utc, Clock.Zone), fueled.Value);
        Assert.Equal(DateTimeKind.Unspecified, fueled.Value.Kind);
        Assert.Equal(DateOnly.FromDateTime(fueled.Value), fueled.Day);
    }

    [Fact]
    public void A_wall_clock_moment_is_kept_as_given()
    {
        var wall = new DateTime(2024, 3, 31, 2, 30, 0, DateTimeKind.Unspecified);

        var fueled = RigFueled.Create(wall);

        Assert.Equal(wall, fueled.Value);
        Assert.Equal(new DateOnly(2024, 3, 31), fueled.Day);
        Assert.Equal(RigFueled.Create(wall), fueled);
    }

    [Fact]
    public void A_strict_range_refuses_an_end_that_is_not_after_its_start_under_the_end()
    {
        var kept = RigKm.Create(10, 20, "Km");

        var refused = Assert.Throws<RaskValidationException>(() => RigKm.Create(10, 10, "Km"));

        Assert.Equal((10, 20), (kept.Start, kept.End));
        Assert.Equal([("Km.End", "It must be after the start, 10.")], Failures(refused));
    }

    [Fact]
    public void A_range_that_is_not_strict_takes_an_equal_end_and_refuses_an_earlier_one()
    {
        var same = RigMonths.Create(3, 3);

        var earlier = Assert.Throws<RaskValidationException>(() => RigMonths.Create(5, 4, "Months"));

        Assert.Equal(3, same.To);
        Assert.Equal([("Months.To", "It cannot be before the start, 5.")], Failures(earlier));
    }

    [Fact]
    public void A_bounded_range_refuses_each_end_outside_its_bounds_under_that_end()
    {
        var refused = Assert.Throws<RaskValidationException>(() => RigMonths.Create(0, 13, "Months"));

        Assert.Equal(
            [("Months.From", "Enter 1 or more."), ("Months.To", "Enter 12 or less.")],
            Failures(refused));
    }

    [Fact]
    public void A_range_with_no_end_is_open_and_is_not_compared()
    {
        var start = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Unspecified);

        var open = RigShift.Create(start, null);
        var refused = Assert.Throws<RaskValidationException>(() => RigShift.Create(start, start.AddHours(-1), "Shift"));

        Assert.Null(open.To);
        Assert.Equal(["Shift.To"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void A_whole_number_bound_on_a_range_of_decimals_is_converted_to_their_type()
    {
        var kept = RigLoad.Create(1.5m, 2m);

        var refused = Assert.Throws<RaskValidationException>(() => RigLoad.Create(0.5m, 2m));

        Assert.Equal(1.5m, kept.Start);
        Assert.Equal(["Start"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void A_range_whose_ends_or_bounds_cannot_be_compared_is_a_programming_error()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var ends = Assert.Throws<ArgumentException>(() => RigMismatch.Create(1, 2));
        var bound = Assert.Throws<ArgumentException>(() => RigWrongBound.TryCreate(day, day, out _, out _));

        Assert.Contains("the start is a Int32, the end a Int64", ends.Message, StringComparison.Ordinal);
        Assert.Contains("this one is a Int32", bound.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_objects_own_rules_are_asked_as_it_is_made_under_the_member_they_name()
    {
        var day = new DateOnly(2026, 3, 1);

        var both = RigOpening.Create(day, 5m);
        var neither = RigOpening.Create(null, null);
        var refused = Assert.Throws<RaskValidationException>(() => RigOpening.Create(null, 5m, "Opening"));

        Assert.Equal(5m, both.Quantity);
        Assert.Null(neither.Date);
        Assert.Equal([("Opening.Quantity", RigOpening.BothOrNeither)], Failures(refused));
    }

    [Fact]
    public void The_constructor_itself_refuses_a_value_its_rules_refuse()
    {
#pragma warning disable RASK127 // what this test proves is that `new` cannot get past the rules either
        var refused = Assert.Throws<RaskValidationException>(() => new RigKm(5, 1));
#pragma warning restore RASK127

        Assert.Equal([("End", "It must be after the start, 5.")], Failures(refused));
    }

    [Fact]
    public void TryCreate_answers_with_the_problems_instead_of_throwing()
    {
        var refused = RigKm.TryCreate(5, 1, out var none, out var problems);
        var kept = RigKm.TryCreate(1, 5, out var km, out var nothing);

        Assert.False(refused);
        Assert.Null(none);
        Assert.Equal([new Problem("End", "It must be after the start, 5.")], problems);
        Assert.True(kept);
        Assert.Equal(5, km!.End);
        Assert.Empty(nothing);
    }

    [Fact]
    public void A_refusal_after_a_TryCreate_throws_again()
    {
        RigAxles.TryCreate(1, out _, out _);

        var refused = Record.Exception(() => RigAxles.Create(1));

        Assert.IsType<RaskValidationException>(refused);
    }

    [Fact]
    public void CreateOptional_is_nothing_for_nothing_and_refuses_half_a_value_under_the_missing_member()
    {
        var start = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Unspecified);

        var empty = RigKm.CreateOptional(null, null, "Km");
        var blank = RigDriver.CreateOptional("  ");
        var noDay = RigInspected.CreateOptional(null);
        var open = RigShift.CreateOptional(start, null);
        var half = Assert.Throws<RaskValidationException>(() => RigKm.CreateOptional(5, null, "Km"));

        Assert.Null(empty);
        Assert.Null(blank);
        Assert.Null(noDay);
        Assert.Null(open!.To);
        Assert.Equal([("Km.End", GeneratedModelRules.RequiredMessage)], Failures(half));
    }

    [Fact]
    public void Two_values_are_equal_by_type_and_value_only()
    {
        var ada = RigDriver.Create(" Ada ");

        var same = RigDriver.Create("Ada");
        var other = RigChip.Create("Ada");

        Assert.Equal(ada, same);
        Assert.Equal(ada.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(ada, RigDriver.Create("Bea"));
        Assert.False(ada.Equals(other));
        Assert.Equal(RigKm.Create(1, 2), RigKm.Create(1, 2));
    }

    [Fact]
    public void A_problem_names_the_member_it_is_written_with()
    {
        var quantity = 5m;

        var at = Problem.At(quantity, "No.");
        var whole = Problem.Whole("No.");

        Assert.Equal("quantity", at.Field);
        Assert.Null(whole.Field);
    }

    // Each failure as the fields it is shown under, joined, and its message.
    private static IEnumerable<(string Fields, string Message)> Failures(RaskValidationException refused) =>
        refused.Failures.Select(f => (string.Join(", ", f.Fields), f.Message));
}
