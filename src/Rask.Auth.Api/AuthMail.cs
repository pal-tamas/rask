using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Mail;

namespace Rask.Auth;

/// <summary>
/// Sends the two emails the account lifecycle needs, through the app's own mail battery.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="IMail" /> is resolved when it is needed rather than injected.</b> The batteries wire
/// auth before mail, so a constructor parameter would be asking for something that is not registered
/// yet — and an app that wires auth by hand may have no mail at all. Asking the provider at send time
/// answers the real question ("can this app send email right now?") instead of a question about
/// startup ordering.
/// </para>
/// <para>
/// A missing mail battery is reported, never swallowed. A password reset that silently sends nothing
/// looks exactly like one that worked, and the person waiting for the email has no way to tell.
/// </para>
/// </remarks>
internal sealed class AuthMail(
    IServiceProvider services,
    AuthOptions options,
    IAuthEmailBodies bodies,
    ILogger<AuthMail> logger)
{
    /// <summary>Whether this app can send at all.</summary>
    public bool IsConfigured => services.GetService<IMail>() is not null;

    /// <summary>Sends the "confirm your address" email. Returns false when the app cannot send.</summary>
    public Task<bool> SendConfirmationAsync(
        string email, string userId, string token, CancellationToken cancellationToken) =>
        SendAsync(
            email,
            options.ConfirmEmailSubject,
            options.ConfirmEmailPath,
            userId,
            token,
            link => bodies.Confirm(link, options.ConfirmEmailSubject),
            cancellationToken);

    /// <summary>Sends the "reset your password" email. Returns false when the app cannot send.</summary>
    public Task<bool> SendPasswordResetAsync(
        string email, string userId, string token, CancellationToken cancellationToken) =>
        SendAsync(
            email,
            options.ResetPasswordSubject,
            options.ResetPasswordPath,
            userId,
            token,
            link => bodies.Reset(link, options.ResetPasswordSubject, options.TokenLifetime),
            cancellationToken);

    private async Task<bool> SendAsync(
        string address,
        string subject,
        string path,
        string userId,
        string token,
        Func<string, string> body,
        CancellationToken cancellationToken)
    {
        // The link is only built once there is mail to carry it, so an app with no mail never asks for an origin.
        if (services.GetService<IMail>() is not { } mail || Origin() is not { } origin)
        {
            return false;
        }

        var link = Link(origin, path, userId, token);

        try
        {
            // Queued on the app's own database, so it survives a restart between "the account exists"
            // and "the email went out" — which matters here more than anywhere: a lost confirmation is
            // an account nobody can use.
            await mail
                .Send(Email.To(address).Subject(subject).Html(body(link)), cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // NOTHING here may escape, and the reason is the caller rather than this method.
            //
            // The confirmation is sent from inside registration, AFTER the account row is committed.
            // A throw at that point reports the registration as failed while the account exists — so
            // the person tries again and is told the address is taken, with no way forward from any
            // page. Losing the email is bad; stranding an account is worse.
            //
            // This is not hypothetical: it is what an app whose DbContext maps the account tables but
            // NOT the mail tables does, and that app has a perfectly good IMail registered. The mail
            // battery's own worker tolerates a table that is not there yet (it must — a fresh app boots
            // before its first migration), which is exactly why the failure surfaces here instead.
            logger.LogError(
                ex,
                "Rask.Auth could not queue a '{Subject}' email. The account operation itself succeeded. "
                + "If this says the mail table is not in the model, add modelBuilder.AddRaskMail() to "
                + "the app's OnModelCreating beside AddRaskAuth(), then create the migration.",
                subject);

            return false;
        }
    }

    /// <summary>Builds the absolute, once-usable link the email carries.</summary>
    /// <remarks>
    /// Both values are URL-encoded. Identity's tokens are base64 and routinely contain <c>+</c> and
    /// <c>/</c>; a <c>+</c> that reaches the query unencoded arrives as a space, and the token then
    /// fails to match in a way that reads as "the link expired" rather than as an encoding bug.
    /// </remarks>
    private static string Link(string origin, string path, string userId, string token) =>
        $"{origin.TrimEnd('/')}{path}"
        + $"?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// The absolute origin an emailed link points at.
    /// </summary>
    /// <remarks>
    /// Three sources, most trustworthy first. <see cref="AuthOptions.PublicOrigin" /> is configuration and
    /// always wins. Failing that, the current request — correct for a single-origin app, and available
    /// because the endpoints and the built-in pages both run inside one. Failing <i>that</i>, the address
    /// the server is actually listening on, which covers a link built from a background context.
    /// <para>
    /// <b>A forwarded host header is never consulted.</b> The host is attacker-controlled on a request
    /// that reaches the app directly, and a reset link built from it would send a valid, working token to
    /// a domain of the attacker's choosing. That is the whole attack, and it is why an app behind a proxy
    /// sets <see cref="AuthOptions.PublicOrigin" /> rather than having it guessed.
    /// </para>
    /// </remarks>
    private string? Origin()
    {
        if (!string.IsNullOrWhiteSpace(options.PublicOrigin))
        {
            return options.PublicOrigin;
        }

        // Past this line the origin is guessed, and outside Development nothing is. The request's Host header is
        // whatever the sender typed: a reset requested with Host: evil.example mails the victim a genuine token on
        // the attacker's domain. The email is not sent; that is logged, never thrown, because the account it is
        // about is already committed (see SendAsync).
        if (services.GetService<IHostEnvironment>() is { } environment && !environment.IsDevelopment())
        {
            logger.LogError(
                "Rask.Auth did not send an emailed link: outside Development it will not take the origin from the "
                + "request, whose Host header is chosen by whoever sends it. Set Rask:Auth:PublicOrigin "
                + "(Rask__Auth__PublicOrigin) to this app's public address, e.g. https://app.example.com; "
                + "`rask deploy --domain` sets it for you.");
            return null;
        }

        if (services.GetService<IHttpContextAccessor>()?.HttpContext?.Request is { } request)
        {
            return $"{request.Scheme}://{request.Host.Value}";
        }

        var addresses = services
            .GetService<IServer>()?.Features
            .Get<IServerAddressesFeature>()?.Addresses;

        // Prefer https when the server is listening on both — a link to the plaintext port would carry
        // a session-granting token over the wire in the clear.
        var listening =
            addresses?.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? addresses?.FirstOrDefault();

        if (listening is not null)
        {
            return listening;
        }

        // Said rather than swallowed. With nothing to prefix, the link goes out relative — which looks
        // fine in the queue and in the .eml, and is simply dead in an inbox. The symptom is a link
        // nobody can click, reported as "the reset email does not work", and nothing in the app says why.
        logger.LogWarning(
            "Rask.Auth is sending a link with no origin to build it from, so it will go out relative and "
            + "will not work from an email client. Set AuthOptions.PublicOrigin to this app's public "
            + "address (app.Configure(c => c.Auth.Configure(o => o.PublicOrigin = \"https://…\"))).");

        return string.Empty;
    }
}
