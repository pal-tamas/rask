using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// .NET 11's asynchronous DataAnnotations in a form: an AsyncValidationAttribute is awaited (never asked
// synchronously — the test attribute throws if it is), an IAsyncValidatableObject's ValidateAsync runs in place of
// its Validate, the field reads as validating while the check is out, a check that throws is a clear error and never
// a pass, and a value changed again cancels the check in flight so only the latest answer lands. The collection owns
// the process-wide diagnostics sink a faulted rule reports to.
[Collection("ConsoleRedirect")]
public partial class AsyncDataAnnotationsTests : global::Rask.Core.RaskMarkup
{
    private const string Taken = "This email is already registered.";

    [Fact]
    public async Task A_taken_email_is_looked_up_asynchronously_and_told_under_the_field()
    {
        var (form, signUp, directory) = SignUpForm(email: "taken@example.com");

        var ok = await form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.Equal([Taken], form.GetValidationMessages(Email(signUp)));
        Assert.Equal(["taken@example.com"], directory.Asked);
    }

    [Fact]
    public async Task A_free_email_passes_the_async_check()
    {
        var (form, signUp, _) = SignUpForm(email: "free@example.com");

        var ok = await form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);

        Assert.True(ok);
        Assert.Empty(form.GetValidationMessages(Email(signUp)));
    }

    [Fact]
    public async Task While_the_lookup_is_out_the_field_reads_as_validating()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (form, signUp, _) = SignUpForm(email: "taken@example.com", taken: (_, _) => gate.Task);

        var check = form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);
        var whileOut = (form.IsValidating(Email(signUp)), form.IsValidatingAny);
        gate.SetResult(true);
        await check;

        Assert.Equal((true, true), whileOut);
        Assert.False(form.IsValidating(Email(signUp)));
        Assert.Equal([Taken], form.GetValidationMessages(Email(signUp)));
    }

    [Fact]
    public async Task A_lookup_that_throws_is_a_clear_error_on_its_field_and_never_a_pass()
    {
        var (form, signUp, _) = SignUpForm(email: "anyone@example.com", taken: (_, _) => throw new InvalidOperationException("the directory is down"));

        var fieldOk = await form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);
        var formOk = await form.Validate(TestContext.Current.CancellationToken);

        Assert.False(fieldOk);
        Assert.False(formOk);
        Assert.Equal(["Validation could not be completed."], form.GetValidationMessages(Email(signUp)));
    }

    [Fact]
    public async Task A_value_changed_again_cancels_the_lookup_in_flight_and_only_the_latest_answer_lands()
    {
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        var (form, signUp, _) = SignUpForm(email: "taken@example.com", taken: (email, token) =>
        {
            if (email == "taken@example.com")
            {
                firstToken = token;
                return first.Task; // ignores its token on purpose: a late answer must still not land
            }

            return Task.FromResult(false);
        });

        var stale = form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);
        signUp.Email = "free@example.com";
        var latest = await form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);
        first.SetResult(true);
        var staleOk = await stale;

        Assert.True(firstToken.IsCancellationRequested);
        Assert.True(latest);
        Assert.False(staleOk);
        Assert.Empty(form.GetValidationMessages(Email(signUp)));
        Assert.False(form.IsValidating(Email(signUp)));
    }

    [Fact]
    public async Task An_empty_required_email_says_so_and_is_never_looked_up()
    {
        var (form, signUp, directory) = SignUpForm(email: "");

        var ok = await form.ValidateField(Email(signUp), TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.Equal(["The Email field is required."], form.GetValidationMessages(Email(signUp)));
        Assert.Empty(directory.Asked);
    }

    [Fact]
    public async Task A_field_with_no_async_rule_is_still_checked_synchronously()
    {
        var (form, signUp, _) = SignUpForm(email: "taken@example.com");
        var name = new FieldIdentifier(signUp, nameof(SignUp.Name));

        var check = form.ValidateField(name, TestContext.Current.CancellationToken);
        var atOnce = (check.IsCompletedSuccessfully, form.IsValidating(name));
        var ok = await check;

        Assert.Equal((true, false), atOnce);
        Assert.False(ok);
    }

    [Fact]
    public async Task The_submit_awaits_the_lookup_and_refuses_a_taken_email()
    {
        var (form, signUp, _) = SignUpForm(email: "taken@example.com");
        signUp.Name = "Ada";

        var ok = await form.Validate(TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.Equal([Taken], form.GetValidationMessages(Email(signUp)));
    }

    [Fact]
    public async Task An_async_validatable_object_is_awaited_in_place_of_its_synchronous_rules()
    {
        var booking = new Booking { Code = "SOLD-OUT" };
        var form = new EditContext(booking);
        form.AddValidator(new DataAnnotationsFieldValidator());
        form.AddValidator(new DataAnnotationsAsyncValidator(null));
        var code = new FieldIdentifier(booking, nameof(Booking.Code));

        var fieldOk = await form.ValidateField(code, TestContext.Current.CancellationToken);
        var formOk = await form.Validate(TestContext.Current.CancellationToken);

        Assert.False(fieldOk);
        Assert.False(formOk);
        Assert.Equal(["That show is sold out."], form.GetValidationMessages(code));
    }

    [Fact]
    public async Task A_form_registers_the_async_half_by_itself_for_a_model_with_an_async_rule()
    {
        var directory = new EmailDirectory((email, _) => Task.FromResult(email == "taken@example.com"));
        var services = new ServiceCollection().AddSingleton(directory).BuildServiceProvider();
        var signUp = new SignUp();
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(signUp)[
            Input.Bind(() => signUp.Email).Id("email"),
            Test.EditContextProbe(c => form = c)
        ], services);

        await page.On("#email").Change("taken@example.com");

        Assert.Equal([Taken], form!.GetValidationMessages(Email(signUp)));
    }

    [Fact]
    public void A_form_over_a_model_with_no_async_rule_stays_synchronous()
    {
        var plain = new Plain();
        EditContext? form = null;

        Page.Render(() => Form.Model(plain)[
            Input.Bind(() => plain.Name).Id("name"),
            Test.EditContextProbe(c => form = c)
        ]);

        Assert.False(form!.HasAsyncValidators);
    }

    [Fact]
    public async Task A_dispatched_request_awaits_its_async_attributes_too()
    {
        var directory = new EmailDirectory((email, _) => Task.FromResult(email == "taken@example.com"));
        var services = new ServiceCollection().AddSingleton(directory).BuildServiceProvider();

        var entries = await DataAnnotationsFieldValidator.ValidateAwaited(
            new SignUp { Email = "taken@example.com", Name = "Ada" }, services, TestContext.Current.CancellationToken);

        Assert.Equal([new ValidationEntry(nameof(SignUp.Email), Taken)], entries);
    }

    private static FieldIdentifier Email(SignUp signUp) => new(signUp, nameof(SignUp.Email));

    private static (EditContext Form, SignUp SignUp, EmailDirectory Directory) SignUpForm(
        string email, Func<string, CancellationToken, Task<bool>>? taken = null)
    {
        var directory = new EmailDirectory(taken ?? ((value, _) => Task.FromResult(value == "taken@example.com")));
        var services = new ServiceCollection().AddSingleton(directory).BuildServiceProvider();
        var signUp = new SignUp { Email = email };
        var form = new EditContext(signUp);
        form.AddValidator(new DataAnnotationsFieldValidator(services));
        form.AddValidator(new DataAnnotationsAsyncValidator(services));
        return (form, signUp, directory);
    }

    public sealed class SignUp
    {
        [Required]
        [EmailAddress]
        [UniqueEmail]
        public string Email { get; set; } = "";

        [Required]
        public string Name { get; set; } = "";
    }

    public sealed class Plain
    {
        [Required]
        public string Name { get; set; } = "";
    }

    public sealed class Booking : IAsyncValidatableObject
    {
        public string Code { get; set; } = "";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
            throw new NotSupportedException("asked synchronously");

        public async IAsyncEnumerable<ValidationResult> ValidateAsync(
            ValidationContext validationContext, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (Code.StartsWith("SOLD", StringComparison.Ordinal))
            {
                yield return new ValidationResult("That show is sold out.", [nameof(Code)]);
            }
        }
    }

    // What an app's directory of users looks like to the rule: the lookup a DbContext would answer.
    public sealed class EmailDirectory(Func<string, CancellationToken, Task<bool>> taken)
    {
        public List<string> Asked { get; } = [];

        public Task<bool> IsTaken(string email, CancellationToken cancellationToken)
        {
            Asked.Add(email);
            return taken(email, cancellationToken);
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UniqueEmailAttribute() : AsyncValidationAttribute("This email is already registered.")
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
            throw new NotSupportedException("asked synchronously");

        protected override async Task<ValidationResult?> IsValidAsync(
            object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            var directory = (EmailDirectory)validationContext.GetService(typeof(EmailDirectory))!;
            return await directory.IsTaken((string)value!, cancellationToken)
                ? new ValidationResult(FormatErrorMessage(validationContext.DisplayName), [validationContext.MemberName!])
                : ValidationResult.Success;
        }
    }
}
