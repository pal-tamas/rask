using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     .NET 11's async DataAnnotations in a real browser over the real socket: an <c>AsyncValidationAttribute</c> on the
///     model, nothing declared on the form. Leaving the field reads "checking" while the lookup is out and then shows
///     what it found; a lookup that throws is "could not be completed" and the form is not saved; a value changed while
///     the lookup is out shows only the latest answer. Then the same form for a Hungarian reader, worded from the app's
///     own resources — <c>AddLocalization()</c> and <c>AddValidation(o =&gt; o.LocalizerProvider = …)</c> — and for an
///     English one, in .NET's own words.
/// </summary>
/// <remarks>
///     The lookup is held by the test (<see cref="EmailLookup" />), so "while it is out" is a state the test chose, not
///     a race against a timer on a loaded machine. Every journey runs at a desktop and a phone width.
/// </remarks>
public sealed class AsyncAttributeTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    public static TheoryData<int> Widths => new() { 1280, 390 };

    public static TheoryData<string, int> Readers => new()
    {
        { "en-US", 1280 }, { "en-US", 390 }, { "hu-HU", 1280 }, { "hu-HU", 390 },
    };

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_taken_email_reads_as_checking_then_is_told_under_the_field_and_a_free_one_is_saved(int width)
    {
        var lookup = new EmailLookup();
        await using var session = await OpenAsync(lookup, width);
        var page = session.Page;

        await page.FillAsync("#email", "ada@rask.sh");
        await page.Locator("#email").BlurAsync();
        await Expect(page.Locator("#checking")).ToHaveTextAsync("checking");
        lookup.Answer("ada@rask.sh", taken: true);
        await Expect(page.Locator("#email-error")).ToHaveTextAsync("ada@rask.sh is already registered.");
        await Expect(page.Locator("#checking")).ToHaveCountAsync(0);
        await page.FillAsync("#email", "new@rask.sh");
        await page.Locator("#email").BlurAsync();
        lookup.Answer("new@rask.sh", taken: false);
        await Expect(page.Locator("#email-error")).ToHaveCountAsync(0);
        lookup.AnswerEveryOtherWith(taken: false);
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=new@rask.sh");
        Assert.Equal(["ada@rask.sh", "new@rask.sh"], lookup.Asked.Distinct());
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_lookup_that_throws_is_told_as_not_completed_and_the_form_is_not_saved(int width)
    {
        var lookup = new EmailLookup();
        await using var session = await OpenAsync(lookup, width);
        var page = session.Page;

        await page.FillAsync("#email", "down@rask.sh");
        await page.Locator("#email").BlurAsync();
        lookup.Fail("down@rask.sh");
        await Expect(page.Locator("#email-error")).ToHaveTextAsync("Validation could not be completed.");
        lookup.FailEveryOther();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#email-error")).ToHaveTextAsync("Validation could not be completed.");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=");
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_value_changed_while_the_lookup_is_out_ends_on_the_latest_values_answer(int width)
    {
        var lookup = new EmailLookup();
        await using var session = await OpenAsync(lookup, width);
        var page = session.Page;

        await page.FillAsync("#email", "ada@rask.sh");
        await page.Locator("#email").BlurAsync();
        await Expect(page.Locator("#checking")).ToHaveTextAsync("checking");
        await page.FillAsync("#email", "new@rask.sh");
        await page.Locator("#email").BlurAsync();
        lookup.Answer("new@rask.sh", taken: false);
        lookup.Answer("ada@rask.sh", taken: true);
        await Expect(page.Locator("#checking")).ToHaveCountAsync(0);

        await Expect(page.Locator("#email-error")).ToHaveCountAsync(0);
        await Expect(page.Locator("#model")).ToHaveTextAsync("email=new@rask.sh");
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public async Task A_hungarian_reader_reads_the_apps_own_resources_and_an_english_one_reads_dotnets_words(string locale, int width)
    {
        var lookup = new EmailLookup();
        lookup.AnswerEveryOtherWith(taken: true);
        await using var session = await HookSession.OpenAsync<SignUpPage>(
            playwright, new() { Locale = locale, ViewportSize = new() { Width = width, Height = 800 } },
            cultures: o =>
            {
                o.SupportedCultures.Add("en");
                o.SupportedCultures.Add("hu");
            },
            services: s => s.AddSingleton(lookup)
                .AddLocalization(o => o.ResourcesPath = "Messages")
                .AddValidation(o => o.LocalizerProvider = static (_, factory) => factory.Create(typeof(ValidationMessages))));
        var page = session.Page;
        var hungarian = locale.StartsWith("hu", StringComparison.Ordinal);

        await page.ClickAsync("#save");
        var empty = await page.Locator("#email-error").TextContentAsync();
        await page.FillAsync("#email", "ada@rask.sh");
        await page.Locator("#email").BlurAsync();
        await Expect(page.Locator("#email-error")).Not.ToHaveTextAsync(empty!);
        var taken = await page.Locator("#email-error").TextContentAsync();

        Assert.Equal(hungarian ? "A(z) E-mail-cím megadása kötelező." : "The E-mail field is required.", empty);
        Assert.Equal(hungarian ? "Ezzel az e-mail-címmel már regisztráltak." : "ada@rask.sh is already registered.", taken);
    }

    private Task<HookSession> OpenAsync(EmailLookup lookup, int width) =>
        HookSession.OpenAsync<SignUpPage>(
            playwright, new() { ViewportSize = new() { Width = width, Height = 800 } },
            services: s => s.AddSingleton(lookup));
}

// The app's directory of users, held by the test: each address waits until the test answers for it.
public sealed class EmailLookup
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _answers = new(StringComparer.Ordinal);
    private Func<string, Task<bool>>? _everyOther;

    public List<string> Asked { get; } = [];

    public Task<bool> IsTaken(string email, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Asked.Add(email);
            return _everyOther?.Invoke(email) ?? Pending(email).Task;
        }
    }

    public void Answer(string email, bool taken)
    {
        lock (_gate)
        {
            Pending(email).TrySetResult(taken);
        }
    }

    public void Fail(string email)
    {
        lock (_gate)
        {
            Pending(email).TrySetException(new TimeoutException("The user directory did not answer."));
        }
    }

    public void AnswerEveryOtherWith(bool taken)
    {
        lock (_gate)
        {
            _everyOther = _ => Task.FromResult(taken);
        }
    }

    public void FailEveryOther()
    {
        lock (_gate)
        {
            _everyOther = _ => Task.FromException<bool>(new TimeoutException("The user directory did not answer."));
        }
    }

    // One answer per address, made the first time either side asks for it.
    private TaskCompletionSource<bool> Pending(string email)
    {
        if (!_answers.TryGetValue(email, out var answer))
        {
            answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _answers[email] = answer;
        }

        return answer;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class UniqueEmailAttribute() : AsyncValidationAttribute("{0} is already registered.")
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        throw new NotSupportedException("Looked up asynchronously.");

    protected override async Task<ValidationResult?> IsValidAsync(
        object? value, ValidationContext validationContext, CancellationToken cancellationToken)
    {
        var lookup = (EmailLookup)validationContext.GetService(typeof(EmailLookup))!;
        return await lookup.IsTaken((string)value!, cancellationToken)
            ? new ValidationResult(FormatErrorMessage((string)value!), [validationContext.MemberName!])
            : ValidationResult.Success;
    }
}

public sealed class SignUp
{
    [Required]
    [EmailAddress]
    [UniqueEmail]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";
}

/// <summary>An email checked when the reader leaves it, with the indicator and the message beside it.</summary>
public sealed partial class SignUpPage : Component
{
    private readonly SignUp _signUp = new();
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["async attribute"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        P.Id("model")[$"email={_signUp.Email}"],
        Form.Model(_signUp).OnSubmit(s => _saved = s.Email)[
            Input.Bind(() => _signUp.Email).Id("email").Blur(),
            Validation.Indicator.Template(() => Span.Id("checking")["checking"]).For(() => _signUp.Email),
            Validation.Message.Template(messages => P.Id("email-error")[messages[0]]).For(() => _signUp.Email),
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ]
    ];
}

// The app's shared validation resources: Messages/ValidationMessages.hu.resx. In the root namespace and outside
// Messages/, so the resources keep the name the localizer looks for.
public sealed class ValidationMessages;
