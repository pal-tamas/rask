using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// The {0} of a DataAnnotations message is the field's [Display] name, the one attribute Rask names a field by;
// a [DisplayName] names nothing, so a field carrying only that keeps its member name.
public class DisplayMessageTests
{
    [Fact]
    public void A_display_attribute_names_the_field_in_dotnets_own_message()
    {
        var person = new Person();

        var said = InEnglish(() => Messages(DataAnnotationsFieldValidator.Validate(person)));

        Assert.Equal("The Given name field is required.", said[nameof(Person.Given)]);
    }

    [Fact]
    public void A_field_named_only_by_display_name_keeps_its_member_name()
    {
        var person = new Person();

        var said = InEnglish(() => Messages(DataAnnotationsFieldValidator.Validate(person)));

        Assert.Equal("The Family field is required.", said[nameof(Person.Family)]);
    }

    [Fact]
    public void A_display_whose_name_is_in_resources_reads_it_from_them()
    {
        var person = new Person();

        var said = InEnglish(() => Messages(DataAnnotationsFieldValidator.Validate(person)));

        Assert.Equal("The field Nickname must be a string with a maximum length of 2.", said[nameof(Person.Alias)]);
    }

    [Fact]
    public void A_field_with_no_attribute_keeps_its_member_name()
    {
        var person = new Person();

        var said = InEnglish(() => Messages(DataAnnotationsFieldValidator.Validate(person)));

        Assert.Equal("The Plain field is required.", said[nameof(Person.Plain)]);
    }

    [Fact]
    public async Task A_form_checking_one_field_names_it_by_its_display_name()
    {
        var person = new Person();
        var given = new FieldIdentifier(person, nameof(Person.Given));
        var form = new EditContext(person);
        form.AddValidator(new DataAnnotationsFieldValidator());

        await InEnglish(() => form.ValidateField(given, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(["The Given name field is required."], form.GetValidationMessages(given));
    }

    [Fact]
    public async Task A_model_with_async_rules_names_its_fields_by_their_display_names_too()
    {
        var signUp = new AwaitedSignUp();

        var said = await InEnglish(() => DataAnnotationsFieldValidator.ValidateAwaited(signUp, null, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("The Work email field is required.", Messages(said)[nameof(AwaitedSignUp.Email)]);
    }

    [Fact]
    public void The_bound_member_of_an_expression_is_named_by_its_attributes()
    {
        var person = new Person();

        var names = (DisplayNames.Of(() => person.Given), DisplayNames.Of(() => person.Family), DisplayNames.Of(() => person.Plain));

        Assert.Equal(("Given name", (string?)null, (string?)null), names);
    }

    private static Dictionary<string, string> Messages(IEnumerable<ValidationEntry> entries) =>
        entries.ToDictionary(entry => entry.Field, entry => entry.Message, StringComparer.Ordinal);

    private static T InEnglish<T>(Func<T> read)
    {
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            return read();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, ui);
        }
    }

    public static class Names
    {
        public static string Nickname => "Nickname";
    }

    private sealed class Person
    {
        [Required, Display(Name = "Given name")] public string? Given { get; set; }

#pragma warning disable RASK131 // The [DisplayName] Rask does not read is what this field is here to prove.
        [Required, DisplayName("Surname")] public string? Family { get; set; }
#pragma warning restore RASK131

        [StringLength(2), Display(Name = nameof(Names.Nickname), ResourceType = typeof(Names))] public string Alias { get; set; } = "toolong";

        [Required] public string? Plain { get; set; }
    }

    private sealed class AwaitedSignUp
    {
        [Required, Display(Name = "Work email"), Free] public string? Email { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class FreeAttribute() : AsyncValidationAttribute("Taken.")
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
            throw new NotSupportedException("asked synchronously");

        protected override Task<ValidationResult?> IsValidAsync(
            object? value, ValidationContext validationContext, CancellationToken cancellationToken) =>
            Task.FromResult(ValidationResult.Success);
    }
}
