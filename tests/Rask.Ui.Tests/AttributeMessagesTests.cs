using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Rask.Core.Forms;

namespace Rask.UiTests;

/// <summary>
///     A DataAnnotations attribute with no message of its own speaks the reader's language: .NET writes those
///     messages in English only, so Rask words them from the <c>RaskStrings</c> catalog — here, the kit's
///     Hungarian — and leaves .NET's text where the catalog has none.
/// </summary>
public class AttributeMessagesTests
{
    [Theory]
    [InlineData(nameof(Everything.Name), "Kötelező kitölteni.")]
    [InlineData(nameof(Everything.Short), "Maximum 5 karakter hosszúnak kell lennie.")]
    [InlineData(nameof(Everything.Code), "Legalább 3, legfeljebb 10 karakter hosszúnak kell lennie.")]
    [InlineData(nameof(Everything.Initials), "Maximum 2 karakter hosszúnak kell lennie.")]
    [InlineData(nameof(Everything.Picks), "Legfeljebb 1 elem adható meg.")]
    [InlineData(nameof(Everything.Pin), "Legalább 4 karakter hosszúnak kell lennie.")]
    [InlineData(nameof(Everything.Pair), "Legalább 2 elemet adjon meg.")]
    [InlineData(nameof(Everything.Iso), "Legalább 2, legfeljebb 3 karakter hosszúnak kell lennie.")]
    [InlineData(nameof(Everything.Tags), "Legalább 1, legfeljebb 2 elemet adjon meg.")]
    [InlineData(nameof(Everything.Ratio), "Az értéknek 1,5 és 10 között kell lennie.")]
    [InlineData(nameof(Everything.Email), "Érvényes e-mail-címet adjon meg.")]
    [InlineData(nameof(Everything.Phone), "Érvényes telefonszámot adjon meg.")]
    [InlineData(nameof(Everything.Site), "Érvényes webcímet adjon meg (http, https vagy ftp).")]
    [InlineData(nameof(Everything.Card), "Érvényes bankkártyaszámot adjon meg.")]
    [InlineData(nameof(Everything.Plate), "A megadott érték formátuma nem megfelelő.")]
    [InlineData(nameof(Everything.Repeated), "Nem egyezik a következővel: Jelszó.")]
    [InlineData(nameof(Everything.Photo), "Csak ilyen kiterjesztésű fájl választható: .png, .jpg.")]
    [InlineData(nameof(Everything.Grade), "Ez az érték nem adható meg.")]
    [InlineData(nameof(Everything.Word), "Ez az érték nem adható meg.")]
    [InlineData(nameof(Everything.Blob), "Érvényes Base64-kódolású szöveget adjon meg.")]
    [InlineData(nameof(Everything.Day), "Érvénytelen érték.")]
    public void Every_built_in_attribute_says_its_failure_in_hungarian_with_its_numbers_filled_in(string field, string hungarian)
    {
        var model = new Everything();

        var said = InHungary(() => Said(model, field));

        Assert.Equal(hungarian, said);
    }

    [Fact]
    public void An_english_reader_reads_dotnets_own_text_word_for_word()
    {
        var model = new Everything();

        var said = In("en-US", () => DataAnnotationsFieldValidator.Validate(model)
            .ToDictionary(entry => entry.Field, entry => entry.Message, StringComparer.Ordinal));

        Assert.Equal("The Name field is required.", said[nameof(Everything.Name)]);
        Assert.Equal("The field Short must be a string with a maximum length of 5.", said[nameof(Everything.Short)]);
        Assert.Equal("The field Ratio must be between 1.5 and 10.", said[nameof(Everything.Ratio)]);
        Assert.Equal("The Email field is not a valid e-mail address.", said[nameof(Everything.Email)]);
        Assert.Equal("'Repeated' and 'Jelszó' do not match.", said[nameof(Everything.Repeated)]);
    }

    [Fact]
    public void An_attribute_with_words_of_its_own_says_them_in_every_language()
    {
        var model = new Worded();

        var hungarian = In("hu-HU", () => DataAnnotationsFieldValidator.Validate(model).Select(entry => entry.Message).ToList());
        var english = In("en-US", () => DataAnnotationsFieldValidator.Validate(model).Select(entry => entry.Message).ToList());

        Assert.Equal(["Adja meg a nevét.", "Ez nem e-mail-cím."], hungarian.Order(StringComparer.Ordinal));
        Assert.Equal(hungarian.Order(StringComparer.Ordinal), english.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_attribute_class_of_the_apps_own_keeps_its_words()
    {
        var model = new Derived();

        var said = KitCulture.In("hu-HU", () => DataAnnotationsFieldValidator.Validate(model).Single().Message);

        Assert.Equal("Nothing here.", said);
    }

    [Fact]
    public void A_field_checked_as_it_is_typed_says_its_failure_in_hungarian_too()
    {
        var model = new Everything();
        var context = new EditContext(model);
        var field = new FieldIdentifier(model, nameof(Everything.Short));

        var said = KitCulture.In("hu-HU", () =>
        {
            new DataAnnotationsFieldValidator().ValidateField(context, field);
            return context.GetValidationMessages(field).Single();
        });

        Assert.Equal("Maximum 5 karakter hosszúnak kell lennie.", said);
    }

    [Fact]
    public void An_attribute_run_by_hand_says_its_failure_in_hungarian_through_the_validator()
    {
        var check = new StringLengthAttribute(5);

        var hungarian = KitCulture.In("hu-HU", () => DataAnnotationsFieldValidator.Message(check, "toolong", "The field Name must be a string with a maximum length of 5."));
        var english = KitCulture.In("en-US", () => DataAnnotationsFieldValidator.Message(check, "toolong", "The field Name must be a string with a maximum length of 5."));

        Assert.Equal("Maximum 5 karakter hosszúnak kell lennie.", hungarian);
        Assert.Equal("The field Name must be a string with a maximum length of 5.", english);
    }

    // A Hungarian reader in Hungary: the words, and the way numbers are written ("1,5").
    private static string InHungary(Func<string> read)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
        try
        {
            return KitCulture.In("hu-HU", read);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // What a reader of the language is told, of any shape: KitCulture.In reads text alone.
    private static T In<T>(string language, Func<T> read)
    {
        var told = default(T)!;
        KitCulture.In(language, () =>
        {
            told = read();
            return string.Empty;
        });
        return told;
    }

    private static string Said(object model, string field) =>
        DataAnnotationsFieldValidator.Validate(model).Single(entry => string.Equals(entry.Field, field, StringComparison.Ordinal)).Message;

    // Every property fails exactly one attribute.
    private sealed class Everything
    {
        [Required] public string? Name { get; set; }

        [StringLength(5)] public string Short { get; set; } = "toolong";

        [StringLength(10, MinimumLength = 3)] public string Code { get; set; } = "ab";

        [MaxLength(2)] public string Initials { get; set; } = "abc";

        [MaxLength(1)] public List<int> Picks { get; set; } = [1, 2];

        [MinLength(4)] public string Pin { get; set; } = "12";

        [MinLength(2)] public int[] Pair { get; set; } = [1];

        [Length(2, 3)] public string Iso { get; set; } = "a";

        [Length(1, 2)] public List<string> Tags { get; set; } = [];

        [Range(1.5, 10)] public double Ratio { get; set; } = 20;

        [EmailAddress] public string Email { get; set; } = "nope";

        [Phone] public string Phone { get; set; } = "call me";

        [Url] public string Site { get; set; } = "nope";

        [CreditCard] public string Card { get; set; } = "1234";

        [RegularExpression("^[A-Z]+$")] public string Plate { get; set; } = "abc";

        [Display(Name = "Jelszó")] public string Password { get; set; } = "secret";

        [Compare(nameof(Password))] public string Repeated { get; set; } = "other";

        [FileExtensions(Extensions = "png,jpg")] public string Photo { get; set; } = "a.gif";

        [AllowedValues("a", "b")] public string Grade { get; set; } = "c";

        [DeniedValues("x")] public string Word { get; set; } = "x";

        [Base64String] public string Blob { get; set; } = "!!";

        [EnumDataType(typeof(DayOfWeek))] public int Day { get; set; } = 42;
    }

    private sealed class Worded
    {
        [Required(ErrorMessage = "Adja meg a nevét.")] public string? Name { get; set; }

        [EmailAddress(ErrorMessage = "Ez nem e-mail-cím.")] public string Email { get; set; } = "nope";
    }

    private sealed class NothingHereAttribute : RequiredAttribute
    {
        public override string FormatErrorMessage(string name) => "Nothing here.";
    }

    private sealed class Derived
    {
        [NothingHere] public string? Name { get; set; }
    }
}
