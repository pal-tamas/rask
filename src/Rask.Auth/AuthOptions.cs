namespace Rask.Auth;

/// <summary>
/// How this app's authentication differs from the default.
/// </summary>
/// <remarks>
/// Every value here already has a working default, so an app that configures nothing still registers,
/// signs in and signs out. What is written here is only the exceptions.
/// <example>
/// <code>
/// app.Configure(c => c.Auth.Configure(o =>
/// {
///     o.CookieName = "shop.auth";
///     o.Bearer = true;              // only when a same-origin cookie cannot serve the caller
/// }));
/// </code>
/// </example>
/// </remarks>
public sealed class AuthOptions
{
    private string _apiPrefix = "/api/auth";
    private int _minimumPasswordLength = 8;

    /// <summary>The name of the authentication cookie.</summary>
    public string CookieName { get; set; } = "rask.auth";

    /// <summary>
    /// Where an unauthenticated visitor is sent. Defaults to <c>/login</c>, which is also
    /// <c>RouteAuthorizationGuard.ChallengePath</c> — the path the route guard has always redirected to.
    /// </summary>
    /// <remarks>
    /// <b>This moves the redirect, not the page.</b> The built-in sign-in page is routed at <c>/login</c>
    /// at compile time, so pointing this somewhere else means putting your own page there — which is the
    /// ordinary way to replace it anyway: declare a component with <c>[Route("…")]</c> and it wins.
    /// </remarks>
    public string LoginPath { get; set; } = "/login";

    /// <summary>Where a visitor creates an account.</summary>
    public string RegisterPath { get; set; } = "/register";

    /// <summary>Where a signed-in visitor signs out.</summary>
    public string LogoutPath { get; set; } = "/logout";

    /// <summary>Where an authenticated but unauthorized visitor is sent.</summary>
    public string AccessDeniedPath { get; set; } = "/forbidden";

    /// <summary>Where a confirmation link lands.</summary>
    public string ConfirmEmailPath { get; set; } = "/confirm-email";

    /// <summary>Where a visitor asks for a password-reset link.</summary>
    public string ForgotPasswordPath { get; set; } = "/forgot-password";

    /// <summary>Where a reset link lands.</summary>
    public string ResetPasswordPath { get; set; } = "/reset-password";

    /// <summary>
    /// Whether an account must confirm its email address before it can sign in. <b>Off by default.</b>
    /// </summary>
    /// <remarks>
    /// Turn it on for production, in one line. It is off by default because a freshly scaffolded app has
    /// no SMTP configured — with the gate on, the first registration would succeed and then be unable to
    /// sign in, including yours, and the confirmation email needed to fix it is the one that cannot be
    /// sent. Off, the confirmation is still sent; it simply does not block the door.
    /// <example>
    /// <code>
    /// app.Configure(c => c.Auth.Configure(o => o.RequireConfirmedEmail = true));
    /// </code>
    /// </example>
    /// </remarks>
    public bool RequireConfirmedEmail { get; set; }

    /// <summary>How long a confirmation or reset link stays valid. Defaults to two hours.</summary>
    /// <remarks>
    /// This is what the email tells the reader, and what Identity's token provider enforces. Both are
    /// set from here so they cannot drift into a message that promises longer than the token allows.
    /// </remarks>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(2);

    /// <summary>The subject line of the confirmation email.</summary>
    public string ConfirmEmailSubject { get; set; } = "Confirm your email address";

    /// <summary>The subject line of the password-reset email.</summary>
    public string ResetPasswordSubject { get; set; } = "Reset your password";

    /// <summary>
    /// The absolute origin to build email links against, when the app cannot know it from a request.
    /// </summary>
    /// <remarks>
    /// A link in an email has to be absolute, and the request that triggers it is a WebSocket frame or
    /// a POST rather than the navigation the visitor will make. Left unset, the origin of the request
    /// that started the flow is used, which is right for a single-origin app. Set it when the app sits
    /// behind a proxy whose public address it cannot otherwise see.
    /// </remarks>
    public string? PublicOrigin { get; set; }

    /// <summary>How long a session stays valid.</summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Whether activity extends the session. On by default.</summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    /// Whether the first account to register becomes an administrator. On by default.
    /// </summary>
    /// <remarks>
    /// It removes the worst onboarding step in self-hosted software — "the app is deployed, now how do I
    /// create the first admin?" — without a seeding migration or a create-admin command. Every account
    /// after the first is an ordinary user. See <see cref="RequireFirstRunToken"/> for the exposure this
    /// opens and how it is closed.
    /// </remarks>
    public bool FirstUserIsAdmin { get; set; } = true;

    /// <summary>
    /// Whether the <b>first</b> registration must present the first-run token. On by default.
    /// </summary>
    /// <remarks>
    /// An app deployed with an empty user table and an open registration page is a land-grab: whoever
    /// reaches it first owns the instance. The token closes that window. It is generated on first
    /// startup, written to the log, and dies the moment an account exists — every registration after the
    /// first is an ordinary open one. Turn this off only where reaching the app at all already proves
    /// you are the operator.
    /// </remarks>
    public bool RequireFirstRunToken { get; set; } = true;

    /// <summary>
    /// The first-run token, when you would rather supply it than read the generated one from the log.
    /// </summary>
    /// <remarks>
    /// Set this from configuration to make a deployment's token predictable — <c>rask deploy</c> does
    /// exactly that so it can print the claim URL. Left <c>null</c>, a cryptographically random one is
    /// generated at startup while the user table is empty.
    /// </remarks>
    public string? FirstRunToken { get; set; }

    /// <summary>The shortest password accepted. Defaults to 8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 6.</exception>
    public int MinimumPasswordLength
    {
        get => _minimumPasswordLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 6);
            _minimumPasswordLength = value;
        }
    }

    /// <summary>Whether a password must contain a digit, a lowercase and an uppercase letter.</summary>
    /// <remarks>
    /// On by default. Length is the property that actually resists guessing, so
    /// <see cref="MinimumPasswordLength"/> is the more useful lever — but composition rules are what most
    /// compliance checklists ask for, and a default that fails an audit is a default people work around.
    /// </remarks>
    public bool RequireMixedCasePasswords { get; set; } = true;

    /// <summary>How many failed attempts lock an account, and for how long.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked after <see cref="MaxFailedAccessAttempts"/>.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The path the <c>register</c>, <c>login</c>, <c>logout</c> and <c>me</c> endpoints sit under.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty, or does not start with <c>/</c>.</exception>
    public string ApiPrefix
    {
        get => _apiPrefix;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (!value.StartsWith('/'))
            {
                throw new ArgumentException(
                    $"The auth API prefix must start with '/', but was '{value}'.", nameof(value));
            }

            _apiPrefix = value.Length > 1 ? value.TrimEnd('/') : value;
        }
    }

    /// <summary>Throws when the options cannot produce a working app.</summary>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(LoginPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(RegisterPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(LogoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfirmEmailPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ForgotPasswordPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ResetPasswordPath);

        if (ExpireTimeSpan <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExpireTimeSpan), ExpireTimeSpan, "The session lifetime must be positive.");
        }

        if (TokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TokenLifetime), TokenLifetime, "The token lifetime must be positive.");
        }
    }
}
