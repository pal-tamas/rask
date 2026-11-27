namespace Rask.Data.Tests;

/// <summary>An email address, at most 200 characters.</summary>
public sealed partial record ContactEmail(string Value) : Email(Value, MaxLength: 200);

/// <summary>A phone number, its length the base's.</summary>
public sealed partial record ContactPhone(string Value) : Phone(Value);

/// <summary>A web address that may be left empty.</summary>
public sealed partial record ContactSite(string Value) : Url(Value, Required: false);

/// <summary>A text of several lines.</summary>
public sealed partial record ContactRemark(string Value) : Multiline(Value, MaxLength: 4000);

/// <summary>A password as it is typed in, at least 8 characters, with a rule of its own.</summary>
public sealed partial record ContactPassword(string Value) : Password(Value, MinLength: 8)
{
    public const string NeedsDigit = "A password has a digit in it.";

    protected override IEnumerable<Problem> Rules()
    {
        if (!Value.Any(char.IsAsciiDigit))
        {
            yield return Problem.Whole(NeedsDigit);
        }
    }
}

/// <summary>A password the reader chooses.</summary>
public sealed partial record ChosenPassword(string Value) : Password(Value, MinLength: 12, New: true);

/// <summary>A contact: one of each kind of text but the password, which is never stored.</summary>
public sealed class Addressee : Aggregate<int>, IHasForm
{
    private Addressee() { }

    public ContactEmail Email { get; private set; } = null!;

    public ContactPhone? Phone { get; private set; }

    public ContactSite? Site { get; private set; }

    public ContactRemark? Remark { get; private set; }

    public static Addressee Of(string email, string? phone = null, string? site = null, string? remark = null) =>
        new()
        {
            Email = ContactEmail.Create(email, nameof(Email)),
            Phone = ContactPhone.CreateOptional(phone, nameof(Phone)),
            Site = ContactSite.CreateOptional(site, nameof(Site)),
            Remark = ContactRemark.CreateOptional(remark, nameof(Remark)),
        };
}
