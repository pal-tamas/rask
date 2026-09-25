using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBannedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // No SP, no enforcement — the rule degrades gracefully when the host hasn't registered
        // the service. ASP.NET Core MVC's own attributes behave the same way when GetService
        // returns null. This means tests that bypass the live render path see the attribute
        // pass for any value; the dedicated DI test pushes a LiveRenderContext to opt in.
        var svc = (IBannedWordService?)validationContext.GetService(typeof(IBannedWordService));
        if (svc is null || value is not string s || s.Length == 0)
        {
            return ValidationResult.Success;
        }

        string[]? members = validationContext.MemberName is null ? null : [validationContext.MemberName];
        return svc.Words.Contains(s, StringComparer.OrdinalIgnoreCase)
            ? new ValidationResult(FormatErrorMessage(s), members)
            : ValidationResult.Success;
    }
}
