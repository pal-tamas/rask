using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Data.Tests;

/// <summary>
/// The kinds of text — Email, Phone, Url, Password, Multiline — checked as the matching attribute checks, stored in a
/// column of the length they declare (or their base's), and shaped for the control bound to them as the attribute
/// would shape it; a password never shown.
/// </summary>
public sealed class TextKindValueObjectTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("a@b")]
    [InlineData("@example.com")]
    [InlineData("ada@")]
    [InlineData("ada@@example.com")]
    [InlineData("ada@exa@mple.com")]
    [InlineData("no address")]
    [InlineData("ada@exa\nmple.com")]
    public void An_email_accepts_exactly_what_EmailAddress_accepts(string given)
    {
        var attribute = new EmailAddressAttribute().IsValid(given);

        var made = ContactEmail.TryCreate(given, out _, out var problems);

        Assert.Equal(attribute, made);
        Assert.Equal(made ? [] : ["Enter a valid email address."], problems.Select(p => p.Message));
    }

    [Theory]
    [InlineData("+36 1 234 5678")]
    [InlineData("+1 (555) 123-4567 ext. 12")]
    [InlineData("06-30-123-4567")]
    [InlineData("call me")]
    [InlineData("+36 1 234 5678 x")]
    [InlineData("12a45")]
    public void A_phone_number_accepts_exactly_what_Phone_accepts(string given)
    {
        var attribute = new PhoneAttribute().IsValid(given);

        var made = ContactPhone.TryCreate(given, out _, out var problems);

        Assert.Equal(attribute, made);
        Assert.Equal(made ? [] : ["Enter a valid phone number."], problems.Select(p => p.Message));
    }

    [Theory]
    [InlineData("https://rask.sh/docs", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com/file", false)]
    [InlineData("example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://", false)]
    public void A_web_address_is_an_absolute_http_or_https_one(string given, bool accepted)
    {
        var made = ContactSite.TryCreate(given, out _, out var problems);

        Assert.Equal(accepted, made);
        Assert.Equal(accepted ? [] : ["Enter a valid http or https address."], problems.Select(p => p.Message));
    }

    [Fact]
    public void A_web_address_left_empty_is_a_value_when_not_required()
    {
        var empty = ContactSite.Create("  ");

        Assert.Equal("", empty.Value);
    }

    [Fact]
    public void A_password_is_kept_as_typed_and_never_shown()
    {
        var password = ContactPassword.Create(" secret 1 ");

        var shown = $"{password}";
        var display = typeof(Password).GetCustomAttributes(typeof(DebuggerDisplayAttribute), inherit: true)
            .Cast<DebuggerDisplayAttribute>().Single();

        Assert.Equal(" secret 1 ", password.Value);
        Assert.Equal("••••", password.ToString());
        Assert.Equal("••••", shown);
        Assert.Equal("••••", display.Value);
        Assert.DoesNotContain("secret", password.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_is_asked_its_lengths_and_its_own_rules()
    {
        var short_ = ContactPassword.TryCreate("abc", out _, out var tooShort);
        var noDigit = ContactPassword.TryCreate("abcdefghij", out _, out var undigited);

        Assert.False(short_);
        Assert.Equal(["Enter at least 8 characters.", ContactPassword.NeedsDigit], tooShort.Select(p => p.Message));
        Assert.False(noDigit);
        Assert.Equal([ContactPassword.NeedsDigit], undigited.Select(p => p.Message));
    }

    [Fact]
    public void Each_kind_tells_its_field_what_the_matching_attribute_would()
    {
        var shapeOf = ShapeOfAddresseeModel();

        var email = shapeOf("Email")!;
        var phone = shapeOf("Phone")!;
        var site = shapeOf("Site")!;
        var remark = shapeOf("Remark")!;

        Assert.Equal((FieldKind.Text, 200, (InputType?)InputType.Email, "email"), (email.Kind, email.MaxLength, email.Input, email.AutoComplete));
        Assert.Equal((32, (InputType?)InputType.Tel, "tel", (InputMode?)InputMode.Tel), (phone.MaxLength, phone.Input, phone.AutoComplete, phone.InputMode));
        Assert.Equal((2048, (InputType?)InputType.Url, (string?)null), (site.MaxLength, site.Input, site.AutoComplete));
        Assert.Equal((4000, FieldHints.MultilineText, (InputType?)null), (remark.MaxLength, remark.Hint, remark.Input));
        Assert.Equal((InputType.Password, "current-password", 8), (ContactPassword.__Field.Input, ContactPassword.__Field.AutoComplete, ContactPassword.__Field.MinLength));
        Assert.Equal("new-password", ChosenPassword.__Field.AutoComplete);
    }

    [Fact]
    public async Task Each_kind_round_trips_as_one_text_column_of_the_length_it_declares_or_its_bases_default()
    {
        // One database for both halves: the suite builds close to EF's twenty service providers already.
        await using var database = await StartDatabaseAsync();
        var contact = Addressee.Of(" ada@example.com ", "+36 1 234 5678", "https://rask.sh", "first line\nsecond line");
        await contact.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Addressee>(contact.Id, TestContext.Current.CancellationToken);

        var type = database.Context.Model.FindEntityType(typeof(Addressee))!;
        Assert.Equal(200, type.FindProperty(nameof(Addressee.Email))!.GetMaxLength());
        Assert.Equal(32, type.FindProperty(nameof(Addressee.Phone))!.GetMaxLength());
        Assert.Equal(2048, type.FindProperty(nameof(Addressee.Site))!.GetMaxLength());
        Assert.Equal(4000, type.FindProperty(nameof(Addressee.Remark))!.GetMaxLength());
        Assert.Equal("ada@example.com", stored!.Email.Value);
        Assert.Equal("+36 1 234 5678", stored.Phone!.Value);
        Assert.Equal("https://rask.sh", stored.Site!.Value);
        Assert.Equal("first line\nsecond line", stored.Remark!.Value);
        Assert.Equal(typeof(string), database.Context.Model.FindEntityType(typeof(Addressee))!.FindProperty(nameof(Addressee.Email))!.GetValueConverter()!.ProviderClrType);
    }

    [Fact]
    public void A_form_bound_to_the_generated_model_asks_the_kinds_format_under_its_field()
    {
        var holder = typeof(AddresseeModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__AddresseeModelRules", StringComparison.Ordinal));
        var ruleOf = holder.GetMethod("Root_RuleOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, Delegate?>>();

        var email = (Validate<string?>)ruleOf("Email")!;

        Assert.Equal(["Enter a valid email address."], email("ada.example.com"));
        Assert.Empty(email("ada@example.com"));
    }

    private static Func<string, FieldShape?> ShapeOfAddresseeModel()
    {
        var holder = typeof(AddresseeModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__AddresseeModelRules", StringComparison.Ordinal));
        return holder.GetMethod("Root_ShapeOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, FieldShape?>>();
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
