using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using Rask.Core.Forms;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

public sealed partial class ValidationDemosTests : global::Rask.Core.RaskMarkup
{
    // --- Render smoke: every demo renders the expected field/button identifiers ---

    [Fact]
    public void ValidationFieldsDemo_renders_all_its_inputs()
    {
        var html = new LiveHost(() => ValidationFieldsDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v1-name", html);
        Assert.Contains("v1-email", html);
        Assert.Contains("v1-age", html);
        Assert.Contains("v1-plan", html);
        Assert.Contains(">Register<", html);
    }

    [Fact]
    public void ValidationSummaryDemo_renders_the_form_buttons_with_no_summary_by_default()
    {
        var html = new LiveHost(() => ValidationSummaryDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v2-name", html);
        // Summary alert is only emitted after a failed submit.
        Assert.DoesNotContain("Please fix", html);
    }

    [Fact]
    public void InlineValidateDemo_renders_the_email_password_and_confirm_form_fields()
    {
        var html = new LiveHost(() => InlineValidateDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v4-email", html);
        Assert.Contains("v4-password", html);
        Assert.Contains("v4-confirm", html);
    }

    [Fact]
    public void NestedAsyncWithLiveTotalsDemo_renders_the_item_rows_and_the_totals_block()
    {
        var html = new LiveHost(() => NestedAsyncWithLiveTotalsDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v-nlive-name", html);
        Assert.Contains("v-nlive-postal", html);
        Assert.Contains("v-nlive-item0-name", html);
        Assert.Contains("v-nlive-item1-name", html);
        Assert.Contains("v-nlive-promo", html);
        Assert.Contains("v-nlive-totals", html);
        // Initial subtotal = 1*9.99 + 2*14.99 = 39.97. Tax 8% on 39.97 = 3.20 (rounded). Total 43.17.
        Assert.Contains("$39.97", html);
        Assert.Contains("$3.20", html);
        Assert.Contains("$43.17", html);
    }

    [Fact]
    public void InlineAsyncValidateDemo_renders_the_promo_input()
    {
        var html = new LiveHost(() => InlineAsyncValidateDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v10-code", html);
        Assert.Contains(">Redeem<", html);
    }

    [Fact]
    public void AsyncValidationDemo_renders_the_username_input()
    {
        var html = new LiveHost(() => AsyncValidationDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v3-username", html);
    }

    [Fact]
    public void CrossFieldSummaryDemo_renders_the_depart_and_return_inputs_and_the_book_button()
    {
        var html = new LiveHost(() => CrossFieldSummaryDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v5-depart", html);
        Assert.Contains("v5-return", html);
        Assert.Contains(">Book<", html);
    }

    [Fact]
    public void ValidatableObjectDemo_renders_the_booking_fields()
    {
        var html = new LiveHost(() => ValidatableObjectDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v11-name", html);
        Assert.Contains("v11-departure", html);
        Assert.Contains("v11-arrival", html);
    }

    [Fact]
    public void ProgrammaticValidateDemo_renders_the_validate_now_and_submit_buttons()
    {
        var html = new LiveHost(() => ProgrammaticValidateDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v6-title", html);
        Assert.Contains("v6-validate-now", html);
        Assert.Contains("v6-submit", html);
    }

    [Fact]
    public void FluentValidationDemo_renders_the_product_and_quantity_inputs()
    {
        var html = new LiveHost(() => FluentValidationDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v7-product", html);
        Assert.Contains("v7-quantity", html);
    }

    [Fact]
    public void FirstErrorWinsDemo_renders_the_license_input()
    {
        var html = new LiveHost(() => FirstErrorWinsDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v8-code", html);
    }

    [Fact]
    public void FluentValidationAsyncDemo_renders_the_ticket_input()
    {
        var html = new LiveHost(() => FluentValidationAsyncDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v9-code", html);
    }

    [Fact]
    public void CustomAttributeDemo_renders_the_username_password_and_confirm_inputs()
    {
        var html = new LiveHost(() => CustomAttributeDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("v12-username", html);
        Assert.Contains("v12-password", html);
        Assert.Contains("v12-confirm", html);
    }

    // --- Model attribute tests (RegistrationModel, SignupModel, etc.) ---

    [Fact]
    public void An_empty_RegistrationModel_fails_required_and_the_age_range()
    {
        var errors = Validate(new RegistrationModel());

        Assert.Contains(errors, e => e.MemberNames.Contains("Name"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Age"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Plan"));
    }

    [Fact]
    public void A_RegistrationModel_with_valid_values_has_no_errors()
    {
        var model = new RegistrationModel { Name = "Pat", Email = "a@b.co", Age = 30, Plan = "pro" };

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void A_SignupModel_with_a_too_short_username_fails_the_string_length_minimum()
    {
        var errors = Validate(new SignupModel { Username = "ab" });

        Assert.Contains(errors, e => e.ErrorMessage!.Contains("3–20"));
    }

    [Fact]
    public void A_TaskModel_with_an_empty_title_fails_required()
    {
        var errors = Validate(new TaskModel { Title = "" });

        Assert.Contains(errors, e => e.MemberNames.Contains("Title"));
    }

    [Fact]
    public void A_LicenseModel_code_in_a_bad_format_fails_the_regex_but_an_empty_one_passes()
    {
        Assert.Empty(Validate(new LicenseModel { Code = "" }));

        var errors = Validate(new LicenseModel { Code = "abc-123" });

        Assert.Contains(errors, e => e.ErrorMessage!.Contains("ABC-123"));
    }

    [Fact]
    public void A_LicenseModel_with_a_valid_code_has_no_errors() => Assert.Empty(Validate(new LicenseModel { Code = "RAS-001" }));

    [Fact]
    public void A_default_TripModel_is_the_edge_case_where_return_equals_depart()
    {
        // The default model values are intentionally equal — used to demonstrate the
        // form-level inline Validate rule firing on submit.
        var m = new TripModel();

        Assert.Equal(m.Depart, m.Return);
    }

    [Fact]
    public void A_BookingModel_with_a_past_departure_yields_a_departure_validation_result()
    {
        var m = new BookingModel
        {
            Name = "X",
            Departure = new DateOnly(2020, 1, 1),
            Arrival = new DateOnly(2020, 1, 5)
        };
        var ctx = new ValidationContext(m);

        var results = m.Validate(ctx).ToList();

        Assert.Contains(results, r => r.MemberNames.Contains("Departure")
                                      && r.ErrorMessage!.Contains("past"));
    }

    [Fact]
    public void A_BookingModel_arriving_on_or_before_departure_yields_a_form_level_error()
    {
        var m = new BookingModel
        {
            Name = "X",
            Departure = new DateOnly(2026, 8, 1),
            Arrival = new DateOnly(2026, 8, 1)
        };
        var ctx = new ValidationContext(m);

        var results = m.Validate(ctx).ToList();

        Assert.Contains(results, r => !r.MemberNames.Any()
                                      && r.ErrorMessage!.Contains("after departure"));
    }

    [Fact]
    public void A_CustomAttributeModel_with_a_weak_password_fails_StrongPassword()
    {
        var errors = Validate(new CustomAttributeModel
        {
            Username = "pat",
            Password = "short",
            ConfirmPassword = "short"
        });

        Assert.Contains(errors, e => e.MemberNames.Contains("Password"));
    }

    [Fact]
    public void A_CustomAttributeModel_with_a_mismatched_confirm_fails()
    {
        var errors = Validate(new CustomAttributeModel
        {
            Username = "pat",
            Password = "Pass1word",
            ConfirmPassword = "DIFFERENT"
        });

        Assert.Contains(errors, e => e.MemberNames.Contains("ConfirmPassword"));
    }

    [Fact]
    public void A_CustomAttributeModel_with_all_valid_values_has_no_errors()
    {
        var errors = Validate(new CustomAttributeModel
        {
            Username = "pat",
            Password = "Pass1word",
            ConfirmPassword = "Pass1word"
        });

        Assert.Empty(errors);
    }

    // --- Custom validator unit tests ---

    [Fact]
    public void StrongPasswordAttribute_rejects_a_short_password_or_one_missing_digits()
    {
        var attr = new StrongPasswordAttribute();

        Assert.False(attr.IsValid("short"));
        Assert.False(attr.IsValid("alllettersonly"));
        Assert.False(attr.IsValid("12345678"));
        Assert.False(attr.IsValid(null));
    }

    [Fact]
    public void StrongPasswordAttribute_accepts_a_mix_of_letters_and_digits()
    {
        var attr = new StrongPasswordAttribute();

        Assert.True(attr.IsValid("Letters99"));
        Assert.True(attr.IsValid("a1234567"));
    }

    [Fact]
    public void MatchesPropertyAttribute_succeeds_on_a_matching_value()
    {
        var attr = new MatchesPropertyAttribute("Other") { ErrorMessage = "no match" };
        var model = new TwoFieldHolder { Value = "x", Other = "x" };
        var ctx = new ValidationContext(model) { MemberName = "Value" };

        var result = InvokeIsValid(attr, model.Value, ctx);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void MatchesPropertyAttribute_errors_on_a_mismatched_value()
    {
        var attr = new MatchesPropertyAttribute("Other") { ErrorMessage = "no match" };
        var model = new TwoFieldHolder { Value = "x", Other = "y" };
        var ctx = new ValidationContext(model) { MemberName = "Value" };

        var result = InvokeIsValid(attr, model.Value, ctx);

        Assert.NotNull(result);
        Assert.Equal("no match", result!.ErrorMessage);
        Assert.Contains("Value", result.MemberNames);
    }

    [Fact]
    public void MatchesPropertyAttribute_returns_an_explanatory_error_for_an_unknown_property()
    {
        var attr = new MatchesPropertyAttribute("DoesNotExist");
        var model = new TwoFieldHolder();
        var ctx = new ValidationContext(model) { MemberName = "Value" };

        var result = InvokeIsValid(attr, "x", ctx);

        Assert.NotNull(result);
        Assert.Contains("DoesNotExist", result!.ErrorMessage);
    }

    [Fact]
    public void NotBannedAttribute_without_a_service_returns_success()
    {
        var attr = new NotBannedAttribute { ErrorMessage = "{0} no" };
        var ctx = new ValidationContext(new object());

        var result = InvokeIsValid(attr, "anything", ctx);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void NotBannedAttribute_with_a_service_fails_a_banned_word_with_a_formatted_message()
    {
        var attr = new NotBannedAttribute { ErrorMessage = "\"{0}\" isn't available." };
        var sp = new ServiceContainer(new BannedWordService());
        var ctx = new ValidationContext(new object(), sp, null) { MemberName = "Username" };

        var result = InvokeIsValid(attr, "admin", ctx);

        Assert.NotNull(result);
        Assert.Equal("\"admin\" isn't available.", result!.ErrorMessage);
        Assert.Contains("Username", result.MemberNames);
    }

    [Fact]
    public void NotBannedAttribute_with_a_service_passes_a_word_that_is_not_banned()
    {
        var attr = new NotBannedAttribute();
        var sp = new ServiceContainer(new BannedWordService());
        var ctx = new ValidationContext(new object(), sp, null);

        Assert.Equal(ValidationResult.Success, InvokeIsValid(attr, "pat", ctx));
    }

    [Fact]
    public void NotBannedAttribute_always_passes_an_empty_string()
    {
        // [Required] handles emptiness; the attribute degrades to pass for "" so the
        // user only sees one error message instead of two stacked.
        var attr = new NotBannedAttribute();
        var sp = new ServiceContainer(new BannedWordService());
        var ctx = new ValidationContext(new object(), sp, null);

        Assert.Equal(ValidationResult.Success, InvokeIsValid(attr, "", ctx));
    }

    [Fact]
    public void BannedWordService_words_contain_the_seed_list()
    {
        var svc = new BannedWordService();

        Assert.Contains("admin", svc.Words);
        Assert.Contains("root", svc.Words);
        Assert.Contains("test", svc.Words);
    }

    // --- FluentValidation tests ---

    [Fact]
    public async Task OrderValidator_fails_both_an_empty_product_and_a_zero_quantity()
    {
        var v = new OrderValidator();

        var result = await v.ValidateAsync(new OrderModel());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Product");
        Assert.Contains(result.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public async Task OrderValidator_passes_valid_values()
    {
        var v = new OrderValidator();

        var result = await v.ValidateAsync(new OrderModel { Product = "Coffee", Quantity = 2 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task TicketValidator_fails_an_empty_code_on_NotEmpty_and_skips_Matches_and_the_async_rule()
    {
        var v = new TicketValidator();

        var result = await v.ValidateAsync(new TicketModel { Code = "" });

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Code is required.", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task TicketValidator_fails_a_bad_format_on_Matches_and_skips_MustAsync()
    {
        var v = new TicketValidator();

        var result = await v.ValidateAsync(new TicketModel { Code = "bad-code" });

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("TKT-123", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task TicketValidator_fails_a_reserved_code_on_MustAsync()
    {
        var v = new TicketValidator();

        var result = await v.ValidateAsync(new TicketModel { Code = "TKT-002" });

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Code is already reserved.", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task TicketValidator_passes_an_available_code()
    {
        var v = new TicketValidator();

        var result = await v.ValidateAsync(new TicketModel { Code = "TKT-999" });

        Assert.True(result.IsValid);
    }

    // --- IAsyncFieldValidator tests ---

    [Fact]
    public async Task UniqueUsernameValidator_adds_a_message_for_a_taken_name()
    {
        var v = new UniqueUsernameValidator();
        var model = new SignupModel { Username = "admin" };
        var ctx = new EditContext(model);

        await v.ValidateAsync(ctx, CancellationToken.None);

        var messages = ctx.GetValidationMessages(new FieldIdentifier(model, "Username")).ToList();
        Assert.Contains(messages, m => m.Contains("already taken"));
    }

    [Fact]
    public async Task UniqueUsernameValidator_adds_no_message_for_an_available_name()
    {
        var v = new UniqueUsernameValidator();
        var model = new SignupModel { Username = "pat" };
        var ctx = new EditContext(model);

        await v.ValidateAsync(ctx, CancellationToken.None);

        var messages = ctx.GetValidationMessages(new FieldIdentifier(model, "Username")).ToList();
        Assert.Empty(messages);
    }

    [Fact]
    public async Task UniqueUsernameValidator_short_circuits_an_empty_name()
    {
        var v = new UniqueUsernameValidator();
        var model = new SignupModel { Username = "" };
        var ctx = new EditContext(model);
        var sw = Stopwatch.StartNew();

        await v.ValidateAsync(ctx, CancellationToken.None);

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200,
            "empty input should short-circuit before the 400ms delay");
    }

    [Fact]
    public async Task UniqueUsernameValidator_throws_InvalidOperationException_on_the_explode_keyword()
    {
        var v = new UniqueUsernameValidator();
        var ctx = new EditContext(new SignupModel { Username = "explode" });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await v.ValidateAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task SlowTitleValidator_adds_a_message_for_a_duplicate_title()
    {
        var v = new SlowTitleValidator();
        var model = new TaskModel { Title = "duplicate" };
        var ctx = new EditContext(model);

        await v.ValidateAsync(ctx, CancellationToken.None);

        var messages = ctx.GetValidationMessages(new FieldIdentifier(model, "Title")).ToList();
        Assert.Contains(messages, m => m.Contains("already used"));
    }

    [Fact]
    public async Task SlowTitleValidator_short_circuits_an_empty_title()
    {
        var v = new SlowTitleValidator();
        var ctx = new EditContext(new TaskModel { Title = "" });
        var sw = Stopwatch.StartNew();

        await v.ValidateAsync(ctx, CancellationToken.None);

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200);
    }

    // --- Helpers ---

    private static List<ValidationResult> Validate(object instance)
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, ctx, results, true);
        return results;
    }

    private static ValidationResult? InvokeIsValid(ValidationAttribute attr, object? value, ValidationContext ctx)
    {
        var mi = typeof(ValidationAttribute).GetMethod("IsValid",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(object), typeof(ValidationContext)], null);
        return (ValidationResult?)mi!.Invoke(attr, [value, ctx]);
    }

    private sealed class TwoFieldHolder
    {
        public string Value { get; set; } = "";
        public string Other { get; set; } = "";
    }

    private sealed class ServiceContainer(IBannedWordService svc) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IBannedWordService) ? svc : null;
    }
}
