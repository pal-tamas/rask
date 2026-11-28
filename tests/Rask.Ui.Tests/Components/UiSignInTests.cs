using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Routing;
using Rask.Wire;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.SignIn</c>: the sign-in card an app places on its <c>/login</c> page — the fields, the refusal under the
///     form, and the way back, over <see cref="IAuth" />. A real sign-in through it is <c>SignInCardTests</c> in
///     <c>Rask.Server.E2E.Tests</c>.
/// </summary>
public partial class UiSignInTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_card_names_the_app_and_asks_for_the_address_and_the_password()
    {
        var page = Draw(() => Ui.SignIn.Title("PPTA"), new Answering(AuthResult.Success));

        var heading = page.TextOf("h1");

        Assert.Equal("PPTA", heading);
        Assert.Equal("email", page.Find("input#email").Attribute("type"));
        Assert.Equal("password", page.Find("input#password").Attribute("type"));
        Assert.True(page.Exists("button#login-submit[type=\"submit\"]"));
        Assert.False(page.Exists("#login-error"));
        Assert.False(page.Exists("#remember"));
    }

    [Fact]
    public void An_app_that_signs_in_by_name_labels_the_first_field_with_its_own_word()
    {
        var page = Draw(() => Ui.SignIn.NameLabel("Felhasználónév"), new Answering(AuthResult.Success));

        var name = page.Find("input#email");

        Assert.NotEqual("email", name.Attribute("type"));
        Assert.Equal("username", name.Attribute("autocomplete"));
        Assert.Contains(page.FindAll("label"), label => label.TextContent.Contains("Felhasználónév", StringComparison.Ordinal));
        Assert.Equal("Sign in", page.TextOf("h1"));
    }

    [Fact]
    public void A_logo_address_is_drawn_as_an_image_above_the_name()
    {
        var page = Draw(() => Ui.SignIn.Title("ESZAF").Logo("/logo.svg"), new Answering(AuthResult.Success));

        var logo = page.Find("[data-ui-sign-in] img");

        Assert.Equal("/logo.svg", logo.Attribute("src"));
    }

    [Fact]
    public async Task A_wrong_password_is_said_under_the_form_without_saying_which_half_was_wrong()
    {
        var auth = new Answering(AuthResult.Fail(AuthError.InvalidCredentials));
        var page = Draw(() => Ui.SignIn, auth);

        await SignIn(page, "ada@example.com", "wrong");

        Assert.Equal("Wrong email or password.", page.TextOf("#login-error"));
        Assert.Equal(("ada@example.com", "wrong"), (auth.Name, auth.Password));
    }

    [Fact]
    public async Task The_throttle_stopping_further_tries_is_said_as_such()
    {
        var page = Draw(() => Ui.SignIn, new Answering(AuthResult.Fail(AuthError.TooManyAttempts)));

        await SignIn(page, "ada@example.com", "wrong");

        Assert.Equal("Too many attempts. Wait a minute and try again.", page.TextOf("#login-error"));
    }

    [Fact]
    public async Task A_refusal_is_said_in_the_readers_language()
    {
        var page = Draw(() => Ui.SignIn.NameLabel("Felhasználónév"), new Answering(AuthResult.Fail(AuthError.InvalidCredentials)));

        await KitCulture.In("hu-HU", () => SignIn(page, "admin", "wrong"));

        Assert.Equal("Hibás felhasználónév vagy jelszó.", page.TextOf("#login-error"));
    }

    [Fact]
    public async Task The_return_address_of_this_site_is_where_the_sign_in_goes_on_to()
    {
        var auth = new Answering(AuthResult.Success);
        var page = Draw(() => Ui.SignIn, auth, returnUrl: "/orders?page=2");

        await SignIn(page, "ada@example.com", "secret");

        Assert.Equal("/orders?page=2", auth.ReturnUrl);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task A_return_address_of_another_site_is_refused_for_the_root(string foreign)
    {
        var auth = new Answering(AuthResult.Success);
        var page = Draw(() => Ui.SignIn, auth, returnUrl: foreign);

        await SignIn(page, "ada@example.com", "secret");

        Assert.Equal("/", auth.ReturnUrl);
    }

    [Fact]
    public async Task Remember_me_is_offered_only_when_asked_for_and_is_sent_with_the_sign_in()
    {
        var auth = new Answering(AuthResult.Success);
        var page = Draw(() => Ui.SignIn.Remember(), auth);

        await page.On("#remember").Change("true");
        await SignIn(page, "ada@example.com", "secret");

        Assert.True(auth.Remember);
    }

    [Fact]
    public void What_the_app_puts_inside_is_drawn_under_the_form()
    {
        var page = Draw(() => Ui.SignIn[A.Id("register").Href("/register")["Create one"]], new Answering(AuthResult.Success));

        var html = page.Html;

        Assert.True(html.IndexOf("id=\"register\"", StringComparison.Ordinal) > html.IndexOf("login-submit", StringComparison.Ordinal));
    }

    private static async Task SignIn(Page page, string name, string password)
    {
        await page.On("#email").Change(name);
        await page.On("#password").Change(password);
        await page.On("form").Submit();
    }

    private static Page Draw(Func<Component?> card, IAuth auth, string? returnUrl = null)
    {
        var route = new RouteState();
        if (returnUrl is not null)
        {
            route.Query = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["returnUrl"] = returnUrl });
        }

        var services = new ServiceCollection()
            .AddSingleton(auth)
            .AddSingleton(route)
            .AddSingleton<IWebAuthn, NoAuthenticator>()
            .BuildServiceProvider();
        return Page.Render(card, services);
    }

    /// <summary>The account store's answer, and what it was asked.</summary>
    private sealed class Answering(AuthResult answer) : IAuth
    {
        public string? Name { get; private set; }

        public string? Password { get; private set; }

        public bool Remember { get; private set; }

        public string? ReturnUrl { get; private set; }

        public Task<AuthResult> SignIn(string email, string password, bool remember = false, string? returnUrl = null)
        {
            (Name, Password, Remember, ReturnUrl) = (email, password, remember, returnUrl);
            return Task.FromResult(answer);
        }

        public Task<AuthResult> Register(string email, string password, string? returnUrl = null, string? firstRunToken = null) =>
            throw new NotSupportedException();

        public Task SignOut(string? returnUrl = null) => throw new NotSupportedException();

        public Task SignOutOtherDevices() => throw new NotSupportedException();

        public Task SignOutEverywhere(string? returnUrl = null) => throw new NotSupportedException();

        public Task<AuthResult> SendPasswordReset(string email) => throw new NotSupportedException();

        public Task<AuthResult> ResetPassword(string userId, string token, string password) => throw new NotSupportedException();

        public Task<AuthResult> ConfirmEmail(string userId, string token) => throw new NotSupportedException();
    }

    /// <summary>A browser with no authenticator.</summary>
    private sealed class NoAuthenticator : IWebAuthn
    {
        public ValueTask<bool> IsSupported() => ValueTask.FromResult(false);

        public ValueTask<bool> IsPlatformAuthenticatorAvailable() => ValueTask.FromResult(false);

        public ValueTask<AttestationResult?> Create(PublicKeyCredentialCreationOptions options) => ValueTask.FromResult<AttestationResult?>(null);

        public ValueTask<AssertionResult?> Get(PublicKeyCredentialRequestOptions options) => ValueTask.FromResult<AssertionResult?>(null);
    }
}
