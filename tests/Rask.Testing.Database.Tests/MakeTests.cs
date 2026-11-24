namespace Rask.Testing.Database.Tests;

/// <summary>Test.Make builds a value object its rules accept, from the rules it hands its base.</summary>
public sealed class MakeTests
{
    [Fact]
    public void A_text_is_filled_to_its_least_length_and_kept_as_its_rules_keep_it()
    {
        var vin = Test.Make<Vin>();
        var initials = Test.Make<Initials>();

        Assert.Equal("ABCDEFGHIJKLMNOPQ", vin.Value);
        Assert.Equal("ABC", initials.Value);
    }

    [Fact]
    public void A_text_meets_a_simple_pattern()
    {
        var tax = Test.Make<TaxNumber>();
        var plate = Test.Make<Plate>();

        Assert.Equal("11111111-1-11", tax.Value);
        Assert.Equal("AAA-111", plate.Value);
    }

    [Fact]
    public void An_email_a_phone_number_and_a_web_address_are_made_in_the_format_their_kind_checks()
    {
        var mail = Test.Make<MailAddress>();
        var mobile = Test.Make<Mobile>();
        var homepage = Test.Make<Homepage>();

        Assert.Equal("sampleabcdef@example.com", mail.Value);
        Assert.Equal("+36 1 234 567", mobile.Value);
        Assert.Equal("https://example.com/", homepage.Value);
    }

    [Fact]
    public void A_pattern_beyond_a_simple_sample_names_the_value_object_and_the_way_out()
    {
        var make = () => Test.Make<Password>();

        var refused = Assert.Throws<InvalidOperationException>(make);

        Assert.Contains("Test.Make<Password>()", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Password.Create(", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_is_its_least_at_its_scale_or_one_within_its_greatest()
    {
        var litres = Test.Make<Litres>();
        var price = Test.Make<Price>();
        var axles = Test.Make<Axles>();
        var debt = Test.Make<Debt>();

        Assert.Equal(0.01m, litres.Value);
        Assert.Equal(1m, price.Value);
        Assert.Equal(2, axles.Value);
        Assert.Equal(-1, debt.Value);
    }

    [Fact]
    public void A_day_or_a_moment_is_its_earliest_or_now_within_its_latest()
    {
        using var clock = Clock.Fake(at: new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

        var inspected = Test.Make<Inspected>();
        var opened = Test.Make<Opened>();
        var fueled = Test.Make<Fueled>();

        Assert.Equal(Clock.Today, inspected.Value);
        Assert.Equal(Opened.First, opened.Value);
        Assert.Equal(Clock.Wall, fueled.Value);
    }

    [Fact]
    public void A_range_runs_from_its_least_one_step_on_within_its_greatest()
    {
        using var clock = Clock.Fake(at: new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

        var km = Test.Make<KmRange>();
        var months = Test.Make<Months>();
        var stay = Test.Make<Stay>();

        Assert.Equal((1, 2), (km.Start, km.End));
        Assert.Equal((12, 12), (months.From, months.To));
        Assert.Equal((Clock.Today, Clock.Today.AddDays(1)), (stay.From, stay.To));
    }

    [Fact]
    public void A_value_object_of_its_own_rules_gets_a_sample_of_each_member()
    {
        using var clock = Clock.Fake(at: new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

        var opening = Test.Make<Opening>();

        Assert.Equal(Clock.Today, opening.Date);
        Assert.Equal(1m, opening.Quantity);
        Assert.Equal(Test.Make<Vin>(), opening.Vehicle);
    }

    [Fact]
    public void A_sample_its_own_rules_refuse_says_what_they_said_and_the_way_out()
    {
        var make = () => Test.Make<Never>();

        var refused = Assert.Throws<InvalidOperationException>(make);

        Assert.Contains(Never.Refusal, refused.Message, StringComparison.Ordinal);
        Assert.Contains("Never.Create(", refused.Message, StringComparison.Ordinal);
    }
}
