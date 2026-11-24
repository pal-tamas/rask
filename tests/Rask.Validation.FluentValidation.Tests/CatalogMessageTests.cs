using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FluentValidation;
using FluentValidation.Resources;
using Rask.Core.Forms;
using Rask.Core.Globalization;

namespace Rask.FluentValidationTests;

// A FluentValidation built-in rule with no message of its own says the RaskStrings sentence the equivalent
// DataAnnotations attribute says through Rask, in the reader's language; a rule the catalog has no sentence for keeps
// FluentValidation's own translation. The catalog and the cultures are process-wide: the suite runs serially
// (AssemblyInfo), and each test puts both back.
public sealed class CatalogMessageTests : IDisposable
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    public CatalogMessageTests() => RaskStrings.UseSource(new Catalog());

    public void Dispose() => RaskStrings.UseSource(null!);

    [Theory]
    [InlineData(nameof(Annotated.Name))]
    [InlineData(nameof(Annotated.Title))]
    [InlineData(nameof(Annotated.Email))]
    [InlineData(nameof(Annotated.Rating))]
    [InlineData(nameof(Annotated.Code))]
    [InlineData(nameof(Annotated.Nick))]
    public async Task A_built_in_rule_says_in_hungarian_what_its_attribute_says(string field)
    {
        var annotated = new Annotated();
        var fluent = new Fluent();

        var attribute = In(Hungarian, () => DataAnnotations(annotated, field));
        var rule = await InAsync(Hungarian, () => FluentMessages(fluent, field));

        Assert.Equal([attribute], rule);
    }

    [Theory]
    [InlineData(nameof(Annotated.Name))]
    [InlineData(nameof(Annotated.Title))]
    [InlineData(nameof(Annotated.Email))]
    [InlineData(nameof(Annotated.Rating))]
    public async Task A_built_in_rule_says_in_an_english_catalog_what_its_attribute_says(string field)
    {
        var annotated = new Annotated();
        var fluent = new Fluent();

        var attribute = In(British, () => DataAnnotations(annotated, field));
        var rule = await InAsync(British, () => FluentMessages(fluent, field));

        Assert.Equal([attribute], rule);
    }

    [Fact]
    public async Task The_catalogs_values_are_the_rules_own()
    {
        var fluent = new Fluent();

        var title = await InAsync(Hungarian, () => FluentMessages(fluent, nameof(Fluent.Title)));
        var rating = await InAsync(Hungarian, () => FluentMessages(fluent, nameof(Fluent.Rating)));

        Assert.Equal(["Maximum 5 karakter hosszúnak kell lennie."], title);
        Assert.Equal(["Az értéknek 1 és 10 között kell lennie."], rating);
    }

    [Fact]
    public async Task A_rules_own_message_wins_over_the_catalog()
    {
        var fluent = new Fluent();

        var said = await InAsync(Hungarian, () => FluentMessages(fluent, nameof(Fluent.Note)));

        Assert.Equal(["Írjon megjegyzést."], said);
    }

    [Fact]
    public async Task A_rule_the_catalog_has_no_sentence_for_keeps_fluent_validations_translation_and_its_name()
    {
        var fluent = new Fluent();

        var said = await InAsync(Hungarian, () => FluentMessages(fluent, nameof(Fluent.Age)));

        Assert.Equal(["A(z) 'Kor' nagyobb kell, hogy legyen, mint '17'."], said);
    }

    [Fact]
    public async Task A_language_the_catalog_does_not_speak_keeps_fluent_validations_text()
    {
        var fluent = new Fluent();

        var said = await InAsync(CultureInfo.GetCultureInfo("de-DE"), () => FluentMessages(fluent, nameof(Fluent.Name)));

        Assert.Equal(["'Name' darf nicht leer sein."], said);
    }

    [Fact]
    public async Task A_language_manager_the_app_sets_itself_is_not_overwritten()
    {
        var fluent = new Fluent();
        await FluentMessages(fluent, nameof(Fluent.Name));
        var rasks = ValidatorOptions.Global.LanguageManager;
        var own = new LanguageManager();
        own.AddTranslation("hu-HU", "NotEmptyValidator", "Az app saját szava.");

        ValidatorOptions.Global.LanguageManager = own;
        string[] said;
        try
        {
            said = await InAsync(Hungarian, () => FluentMessages(fluent, nameof(Fluent.Name)));
        }
        finally
        {
            ValidatorOptions.Global.LanguageManager = rasks;
        }

        Assert.Equal(["Az app saját szava."], said);
    }

    private static string DataAnnotations(Annotated model, string field) =>
        Assert.Single(DataAnnotationsFieldValidator.Validate(model), entry => entry.Field == field).Message;

    private static async Task<string[]> FluentMessages(Fluent model, string field)
    {
        var form = RegisterValidator(model, new FluentRules());
        await form.Validate(TestContext.Current.CancellationToken);
        return [.. form.GetValidationMessages(new FieldIdentifier(model, field))];
    }

    private static T In<T>(CultureInfo culture, Func<T> run)
    {
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        try
        {
            return run();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);
        }
    }

    private static async Task<T> InAsync<T>(CultureInfo culture, Func<Task<T>> run)
    {
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        try
        {
            return await run();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);
        }
    }

    public sealed class Annotated
    {
        [Required] public string? Name { get; set; }

        [StringLength(5)] public string Title { get; set; } = "Too long";

        [EmailAddress] public string Email { get; set; } = "nope";

        [Range(1, 10)] public int Rating { get; set; }

        [RegularExpression("^[A-Z]+$")] public string Code { get; set; } = "abc";

        [StringLength(10, MinimumLength = 3)] public string Nick { get; set; } = "ab";
    }

    public sealed class Fluent
    {
        public string? Name { get; set; }

        public string Title { get; set; } = "Too long";

        public string Email { get; set; } = "nope";

        public int Rating { get; set; }

        public string Code { get; set; } = "abc";

        public string Nick { get; set; } = "ab";

        public string Note { get; set; } = "";

        public int Age { get; set; } = 3;
    }

    private sealed class FluentRules : AbstractValidator<Fluent>
    {
        public FluentRules()
        {
            RuleFor(f => f.Name).NotEmpty();
            RuleFor(f => f.Title).MaximumLength(5);
            RuleFor(f => f.Email).EmailAddress();
            RuleFor(f => f.Rating).InclusiveBetween(1, 10);
            RuleFor(f => f.Code).Matches("^[A-Z]+$");
            RuleFor(f => f.Nick).Length(3, 10);
            RuleFor(f => f.Note).NotEmpty().WithMessage("Írjon megjegyzést.");
            RuleFor(f => f.Age).GreaterThan(17).WithName("Kor");
        }
    }

    // The app's RaskStrings catalog: Rask.Ui's Hungarian, and English sentences for a British reader.
    private sealed class Catalog : IRaskStringSource
    {
        private static readonly Dictionary<RaskString, string> HungarianSentences = new()
        {
            [RaskString.ValidationRequired] = "Kötelező kitölteni.",
            [RaskString.ValidationTooLong] = "Maximum {0} karakter hosszúnak kell lennie.",
            [RaskString.ValidationLength] = "Legalább {0}, legfeljebb {1} karakter hosszúnak kell lennie.",
            [RaskString.ValidationBetween] = "Az értéknek {0} és {1} között kell lennie.",
            [RaskString.ValidationEmail] = "Érvényes e-mail-címet adjon meg.",
            [RaskString.ValidationPattern] = "A megadott érték formátuma nem megfelelő.",
        };

        private static readonly Dictionary<RaskString, string> EnglishSentences = new()
        {
            [RaskString.ValidationRequired] = "This field is required.",
            [RaskString.ValidationTooLong] = "Enter at most {0} characters.",
            [RaskString.ValidationBetween] = "Enter a value between {0} and {1}.",
            [RaskString.ValidationEmail] = "Enter a valid email address.",
        };

        public string? Get(RaskString key, string cultureTag) => cultureTag switch
        {
            "hu-HU" => HungarianSentences.GetValueOrDefault(key),
            "en-GB" => EnglishSentences.GetValueOrDefault(key),
            _ => null,
        };
    }
}
