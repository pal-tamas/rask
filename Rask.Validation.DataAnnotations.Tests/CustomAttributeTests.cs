using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-only NoOpRoot subclass has no generated factory

namespace Rask.Validation.DataAnnotations.Tests;

// Custom ValidationAttribute subclasses are walked by System.ComponentModel.DataAnnotations.Validator
// at validation time — DataAnnotationsValidator doesn't need to know about them. These tests pin:
//   • IsValid(object?) overrides fire and produce per-field messages.
//   • GetValidationResult(object?, ValidationContext) overrides can read ObjectInstance
//     (cross-field) and MemberName (per-field path).
//   • ValidationContext.GetService<T>() resolves from the render-scoped LiveRenderContext.Services
//     when one is active, and degrades to null otherwise (ASP.NET Core parity).
public class CustomAttributeTests
{
    [Fact]
    public void IsValid_CustomAttribute_AddsMessage_WhenInvalid()
    {
        var m = new Account { Password = "weak" };
        var ctx = RegisterValidator(m);

        ctx.Validate();

        Assert.Contains(
            "8+ chars, letters and digits.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Account.Password))));
    }

    [Fact]
    public void IsValid_CustomAttribute_DoesNotAddMessage_WhenValid()
    {
        var m = new Account { Username = "alice", Password = "Strong1Pass", ConfirmPassword = "Strong1Pass" };
        var ctx = RegisterValidator(m);

        Assert.True(ctx.Validate());
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void GetValidationResult_CustomAttribute_UsesObjectInstance()
    {
        // MatchesProperty reads ValidationContext.ObjectInstance to fetch the sibling Password
        // value and compare. With mismatched values the rule must fire on ConfirmPassword.
        var m = new Account { Username = "alice", Password = "Strong1Pass", ConfirmPassword = "Different1" };
        var ctx = RegisterValidator(m);

        ctx.Validate();

        Assert.Contains(
            "Passwords don't match.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Account.ConfirmPassword))));

        // When they match the rule must NOT fire.
        m.ConfirmPassword = m.Password;
        var ctx2 = RegisterValidator(m);
        Assert.True(ctx2.Validate());
    }

    [Fact]
    public void GetValidationResult_CustomAttribute_UsesMemberName_OnFieldPath()
    {
        // ValidateField sets ValidationContext.MemberName so the attribute can decide which
        // field its result lands on. Assert the message lands on ConfirmPassword only.
        var m = new Account { Username = "alice", Password = "Strong1Pass", ConfirmPassword = "Different1" };
        var ctx = RegisterValidator(m);

        ctx.ValidateField(new FieldIdentifier(m, nameof(Account.ConfirmPassword)));

        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Account.ConfirmPassword))));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Account.Password))));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public void GetValidationResult_CustomAttribute_CanResolveServices_ViaValidationContext()
    {
        // [Banned] resolves IBannedWords from ValidationContext.GetService — proves the
        // render-scoped IServiceProvider flows through ValidationContext construction. The
        // validator snapshots LiveRenderContext.Current?.Services at registration time (which
        // is the Render() pass), so the live context must be active around RegisterValidator,
        // NOT around Validate (handler invocation doesn't re-enter LiveRenderContext, just
        // like the production path).
        var sp = new StubServices(new BannedWords("admin", "root"));
        var m = new Account { Username = "admin", Password = "Strong1Pass", ConfirmPassword = "Strong1Pass" };

        EditContext ctx;
        using (LiveRenderContext.Begin(new NoOpRoot(), sp))
        {
            ctx = RegisterValidator(m);
        }

        ctx.Validate();

        Assert.Contains(
            "\"admin\" isn't available.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Account.Username))));
    }

    [Fact]
    public void Validate_NoServiceProvider_StillRuns_AttributeSeesNullGetService()
    {
        // Outside a live context, GetService returns null. The Banned attribute is defensive
        // (returns Success when the service is missing) so the form must validate as if the
        // rule weren't present — same as ASP.NET Core when MVC runs without a configured SP.
        var m = new Account { Username = "admin", Password = "Strong1Pass", ConfirmPassword = "Strong1Pass" };
        var ctx = RegisterValidator(m);

        ctx.Validate();

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Account.Username))));
    }

    private static EditContext RegisterValidator(object model)
    {
        var ctx = new EditContext(model);
        using (EditContextScope.Push(ctx))
        {
            DataAnnotationsValidator().ToHtml();
        }

        return ctx;
    }

    private sealed class NoOpRoot : Component
    {
        protected override Component Render() => Fragment();
    }

    private sealed class StubServices : IServiceProvider
    {
        private readonly object _value;
        public StubServices(object value) => _value = value;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IBannedWords) ? _value : null;
    }

    private sealed class Account
    {
        [Banned(ErrorMessage = "\"{0}\" isn't available.")]
        public string Username { get; set; } = "";

        [Required]
        [StrongPassword(ErrorMessage = "8+ chars, letters and digits.")]
        public string Password { get; set; } = "";

        [MatchesProperty(nameof(Password), ErrorMessage = "Passwords don't match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}

// Declared at namespace scope so the attribute can target a fixture property.
internal interface IBannedWords
{
    bool IsBanned(string word);
}

internal sealed class BannedWords : IBannedWords
{
    private readonly HashSet<string> _set;

    public BannedWords(params string[] words) =>
        _set = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

    public bool IsBanned(string word) => _set.Contains(word);
}

internal sealed class StrongPasswordAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string s || s.Length < 8)
        {
            return false;
        }

        bool letter = false, digit = false;
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                letter = true;
            }
            else if (char.IsDigit(ch))
            {
                digit = true;
            }

            if (letter && digit)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class MatchesPropertyAttribute(string otherProperty) : ValidationAttribute
{
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Test-fixture model is preserved by the test project's compile-time references.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
    {
        var sibling = ctx.ObjectInstance.GetType().GetProperty(otherProperty)?.GetValue(ctx.ObjectInstance);
        return Equals(value, sibling)
            ? ValidationResult.Success
            : new ValidationResult(
                ErrorMessage ?? $"Must match {otherProperty}.",
                ctx.MemberName is null ? null : new[] { ctx.MemberName });
    }
}

internal sealed class BannedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
    {
        var svc = ctx.GetService(typeof(IBannedWords)) as IBannedWords;
        if (svc is null || value is not string s)
        {
            return ValidationResult.Success;
        }

        return svc.IsBanned(s)
            ? new ValidationResult(FormatErrorMessage(s),
                ctx.MemberName is null ? null : new[] { ctx.MemberName })
            : ValidationResult.Success;
    }
}
