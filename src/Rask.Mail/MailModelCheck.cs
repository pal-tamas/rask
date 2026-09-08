using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Mail;

/// <summary>The mail queue's claim on the application's model, checked once at boot. See #1015.</summary>
/// <remarks>
/// This is the battery the issue was written about: an app that maps <c>AddRaskAuth()</c> and forgets
/// <c>AddRaskMail()</c> signs people in perfectly well and then dies on the first password reset
/// anybody asks for, because that is the first foreground call that touches <see cref="QueuedMail" />.
/// </remarks>
internal sealed class MailModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Mail";

    protected override Type Entity => typeof(QueuedMail);

    protected override string MapCall => "AddRaskMail";
}
