using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Rask.Core.Forms;

namespace Rask.FluentValidationTests;

// A FluentValidation message names its field by the [Display] or [DisplayName] the field's label and its
// DataAnnotations messages read, [Display] first; a rule's WithName still has the last word.
public class DisplayNameTests
{
    [Fact]
    public async Task A_display_name_attribute_names_the_field_in_a_fluent_message()
    {
        var contact = new Contact();
        var form = RegisterValidator(contact, new ContactValidator());

        await form.Validate(TestContext.Current.CancellationToken);

        Assert.Equal(["'Given name' must not be empty."], form.GetValidationMessages(new FieldIdentifier(contact, nameof(Contact.Given))));
    }

    [Fact]
    public async Task Display_wins_over_display_name_in_a_fluent_message()
    {
        var contact = new Contact();
        var form = RegisterValidator(contact, new ContactValidator());

        await form.Validate(TestContext.Current.CancellationToken);

        Assert.Equal(["'Family name' must not be empty."], form.GetValidationMessages(new FieldIdentifier(contact, nameof(Contact.Family))));
    }

    [Fact]
    public async Task A_rules_own_name_wins_over_the_attributes()
    {
        var contact = new Contact();
        var form = RegisterValidator(contact, new ContactValidator());

        await form.Validate(TestContext.Current.CancellationToken);

        Assert.Equal(["'Handle' must not be empty."], form.GetValidationMessages(new FieldIdentifier(contact, nameof(Contact.Nick))));
    }

    [Fact]
    public async Task A_field_with_neither_attribute_keeps_fluent_validations_own_name()
    {
        var contact = new Contact();
        var form = RegisterValidator(contact, new ContactValidator());

        await form.Validate(TestContext.Current.CancellationToken);

        Assert.Equal(["'Home Town' must not be empty."], form.GetValidationMessages(new FieldIdentifier(contact, nameof(Contact.HomeTown))));
    }

    public sealed class Contact
    {
        [DisplayName("Given name")] public string Given { get; set; } = "";

        [Display(Name = "Family name"), DisplayName("Surname")] public string Family { get; set; } = "";

        [Display(Name = "Nickname")] public string Nick { get; set; } = "";

        public string HomeTown { get; set; } = "";
    }

    private sealed class ContactValidator : AbstractValidator<Contact>
    {
        public ContactValidator()
        {
            RuleFor(c => c.Given).NotEmpty();
            RuleFor(c => c.Family).NotEmpty();
            RuleFor(c => c.Nick).NotEmpty().WithName("Handle");
            RuleFor(c => c.HomeTown).NotEmpty();
        }
    }
}
