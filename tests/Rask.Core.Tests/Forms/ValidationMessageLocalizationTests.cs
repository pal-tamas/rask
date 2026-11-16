using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Rask.Core.Forms;
using Rask.Core.Globalization;
using Rask.Core.Tests.Globalization;

namespace Rask.Core.Tests.Forms;

// A failed DataAnnotations attribute words itself from the app's own resources, .NET 11's way: the app calls
// AddLocalization() and AddValidation(o => o.LocalizerProvider = ...), and the keys .NET's validation pipeline asks —
// the attribute's ErrorMessage, then {Type}_{Member}_{Attribute}_Error, {Type}_{Attribute}_Error, {Attribute}_Error —
// are the ones a form asks, filled in by the attribute's own FormatMessage. Those resources are the app speaking, so
// they win over Rask's RaskStrings catalog, which still words whatever they have no key for. One process-wide catalog
// and culture, so these share the culture collection.
[Collection(CultureCollection.Name)]
public sealed class ValidationMessageLocalizationTests : IDisposable
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => RaskStrings.ResetForTests();

    [Fact]
    [RestoreCulture]
    public void A_hungarian_reader_reads_the_apps_resources_and_an_english_one_reads_dotnets_text()
    {
        var services = AppWithResources();
        var person = new Person { Age = 200 };

        var hungarian = In(Hungarian, () => Messages(person, services));
        var english = In(English, () => Messages(person, services));

        Assert.Equal("A(z) Név megadása kötelező.", hungarian[nameof(Person.Name)]);
        Assert.Equal("A(z) Age értéke 18 és 120 között legyen.", hungarian[nameof(Person.Age)]);
        Assert.Equal("The Név field is required.", english[nameof(Person.Name)]);
        Assert.Equal("The field Age must be between 18 and 120.", english[nameof(Person.Age)]);
    }

    [Fact]
    [RestoreCulture]
    public void A_literal_display_name_is_looked_up_in_the_same_resources()
    {
        var services = AppWithResources();
        var contact = new Contact();

        var said = In(Hungarian, () => Messages(contact, services));

        Assert.Equal("A(z) E-mail-cím megadása kötelező.", said[nameof(Contact.Email)]);
    }

    [Fact]
    [RestoreCulture]
    public void The_member_key_is_asked_before_the_type_key_and_the_type_key_before_the_attributes_own()
    {
        var services = AppWith(new Dictionary<string, string>
        {
            ["Order_Reference_RequiredAttribute_Error"] = "member",
            ["Order_RequiredAttribute_Error"] = "type",
            ["RequiredAttribute_Error"] = "attribute",
        });

        var said = In(Hungarian, () => Messages(new Order(), services));
        var other = In(Hungarian, () => Messages(new Person { Age = 30 }, services));

        Assert.Equal("member", said[nameof(Order.Reference)]);
        Assert.Equal("type", said[nameof(Order.Note)]);
        Assert.Equal("attribute", other[nameof(Person.Name)]);
    }

    [Fact]
    [RestoreCulture]
    public void An_error_message_of_the_apps_own_is_itself_the_first_key()
    {
        var services = AppWith(new Dictionary<string, string>
        {
            ["Tell us your street."] = "Adja meg az utcát.",
            ["Address_Street_RequiredAttribute_Error"] = "not this one",
        });

        var said = In(Hungarian, () => Messages(new Address(), services));

        Assert.Equal("Adja meg az utcát.", said[nameof(Address.Street)]);
    }

    [Fact]
    [RestoreCulture]
    public void The_apps_resources_win_over_rasks_catalog_which_still_words_what_they_lack()
    {
        RaskStrings.UseSource(new Catalog((RaskString.ValidationRequired, "Kötelező kitölteni."), (RaskString.ValidationEmail, "Érvényes e-mail-címet adjon meg.")));
        var services = AppWith(new Dictionary<string, string> { ["RequiredAttribute_Error"] = "A(z) {0} kötelező." });

        var said = In(Hungarian, () => Messages(new Contact { Email = "not-an-email", Name = "" }, services));

        Assert.Equal("A(z) Name kötelező.", said[nameof(Contact.Name)]);
        Assert.Equal("Érvényes e-mail-címet adjon meg.", said[nameof(Contact.Email)]);
    }

    [Fact]
    [RestoreCulture]
    public async Task An_async_rules_message_comes_from_the_same_resources()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Messages")
            .AddValidation(o => o.LocalizerProvider = static (_, factory) => factory.Create(typeof(ValidationMessages)))
            .AddSingleton(new AsyncDataAnnotationsTests.EmailDirectory((_, _) => Task.FromResult(true)))
            .BuildServiceProvider();
        CultureInfo.CurrentUICulture = Hungarian;

        var entries = await DataAnnotationsFieldValidator.ValidateAwaited(
            new SignUp { Email = "taken@example.com" }, services, TestContext.Current.CancellationToken);

        Assert.Equal([new ValidationEntry(nameof(SignUp.Email), "Ezzel az e-mail-címmel már regisztráltak.")], entries);
    }

    [Fact]
    [RestoreCulture]
    public void Without_a_localizer_nothing_changes()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var said = In(Hungarian, () => Messages(new Person { Age = 30 }, services));

        Assert.Equal("The Név field is required.", said[nameof(Person.Name)]);
    }

    private static Dictionary<string, string> Messages(object model, IServiceProvider services) =>
        DataAnnotationsFieldValidator.Validate(model, services).ToDictionary(e => e.Field, e => e.Message, StringComparer.Ordinal);

    private static T In<T>(CultureInfo culture, Func<T> read)
    {
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, culture);
        return read();
    }

    // What an app writes: the platform's two calls, its shared resources in Messages/ValidationMessages.hu.resx.
    private static ServiceProvider AppWithResources() =>
        new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Messages")
            .AddValidation(o => o.LocalizerProvider = static (_, factory) => factory.Create(typeof(ValidationMessages)))
            .BuildServiceProvider();

    private static ServiceProvider AppWith(Dictionary<string, string> hungarian) =>
        new ServiceCollection()
            .AddSingleton<IStringLocalizerFactory>(new Fixed(hungarian))
            .AddValidation()
            .BuildServiceProvider();

    public sealed class Person
    {
        [Required]
        [Display(Name = "Név")]
        public string Name { get; set; } = "";

        [Range(18, 120)]
        public int Age { get; set; }
    }

    public sealed class Contact
    {
        [Required]
        [EmailAddress]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = "";

        [Required]
        public string Name { get; set; } = "x";
    }

    public sealed class Order
    {
        [Required]
        public string Reference { get; set; } = "";

        [Required]
        public string Note { get; set; } = "";
    }

    public sealed class Address
    {
        [Required(ErrorMessage = "Tell us your street.")]
        public string Street { get; set; } = "";
    }

    public sealed class SignUp
    {
        [AsyncDataAnnotationsTests.UniqueEmail]
        public string Email { get; set; } = "";
    }

    // Hungarian only: every other language finds no key, as an app with one resource file does.
    private sealed class Fixed(Dictionary<string, string> hungarian) : IStringLocalizerFactory, IStringLocalizer
    {
        public LocalizedString this[string name] =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hu" && hungarian.TryGetValue(name, out var text)
                ? new LocalizedString(name, text)
                : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        public IStringLocalizer Create(Type resourceSource) => this;

        public IStringLocalizer Create(string baseName, string location) => this;
    }

    private sealed class Catalog(params (RaskString Key, string Text)[] texts) : IRaskStringSource
    {
        public string? Get(RaskString key, string cultureTag) =>
            cultureTag.StartsWith("hu", StringComparison.Ordinal) ? texts.FirstOrDefault(text => text.Key == key).Text : null;
    }
}
