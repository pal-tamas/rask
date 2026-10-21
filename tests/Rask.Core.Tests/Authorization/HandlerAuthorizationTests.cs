using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Authorization;

// [Authorize] on the method an event runs: a person who does not meet it is told so in a toast, the method never
// runs, and the page stays as it was. Whether the method was handed over as a method group or called from a
// lambda, and whatever the browser posts. The collection owns the process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class HandlerAuthorizationTests : global::Rask.Core.RaskMarkup
{
    private const string Forbidden = "You do not have permission to do that.";

    [Fact]
    public async Task A_method_group_with_authorize_is_refused_with_a_toast_and_does_not_run()
    {
        var page = new Trailers(Person("ana", "driver"));
        using var toasts = Toast.Fake();

        await page.Click("by-method-group");

        Assert.Equal(0, page.Removed);
        toasts.Shown(Forbidden).Once();
        toasts.Shown(Forbidden).As(ToastLevel.Error);
        Assert.Contains("<li>ABC-123</li>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lambda_that_calls_a_method_with_authorize_is_refused_the_same_way()
    {
        var page = new Trailers(Person("ana", "driver"));
        using var toasts = Toast.Fake();

        await page.Click("by-lambda");

        Assert.Equal(0, page.Removed);
        Assert.Equal(0, page.SaidRemoved);
        toasts.Shown(Forbidden).Once();
    }

    [Theory]
    [InlineData("by-method-group")]
    [InlineData("by-lambda")]
    public async Task A_user_with_the_role_runs_it(string button)
    {
        var page = new Trailers(Person("bea", "admin"));
        using var toasts = Toast.Fake();

        await page.Click(button);

        Assert.Equal(1, page.Removed);
        toasts.Shown(Forbidden).Never();
        Assert.DoesNotContain("<li>ABC-123</li>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_id_posted_by_hand_is_held_to_the_same_rule()
    {
        var page = new Trailers(Person("ana", "driver"));
        using var toasts = Toast.Fake();
        var id = ClickOf(page.Html, "by-method-group");

        var handled = await page.Post(id);

        Assert.True(handled);
        Assert.Equal(0, page.Removed);
        toasts.Shown(Forbidden).Once();
    }

    [Fact]
    public async Task A_method_group_handed_to_a_components_callback_is_refused_when_the_component_raises_it()
    {
        var page = new Trailers(Person("ana", "driver"));
        using var toasts = Toast.Fake();

        await page.Click("through-a-component");

        Assert.Equal(0, page.Removed);
        toasts.Shown(Forbidden).Once();
    }

    [Fact]
    public async Task Nobody_signed_in_is_refused_a_method_that_only_asks_for_a_signed_in_user()
    {
        var page = new Trailers(new ClaimsPrincipal(new ClaimsIdentity()));
        using var toasts = Toast.Fake();

        await page.Click("signed-in-only");

        Assert.Equal(0, page.Archived);
        toasts.Shown(Forbidden).Once();
    }

    [Fact]
    public async Task A_refused_attempt_is_logged_at_information_naming_the_method_and_nothing_of_the_person()
    {
        var page = new Trailers(Person("ana", "driver"));
        using var toasts = Toast.Fake();
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;

        try
        {
            await page.Click("by-method-group");
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        var line = Assert.Single(captured);
        Assert.Equal(RaskLogLevel.Information, line.Level);
        Assert.Contains("TrailerList.Remove was refused", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ana", line.Message, StringComparison.Ordinal);
    }

    // The click handler id of the element with this id: the id comes first, the data-* after it.
    private static string ClickOf(string html, string id) =>
        System.Text.RegularExpressions.Regex.Match(html, $"id=\"{id}\"[^>]*?data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;

    private static ClaimsPrincipal Person(string name, string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, role)], "test"));

    // A page as a host mounts it, for the person the test names.
    private sealed class Trailers
    {
        private readonly RootErrorBoundary _root;
        private readonly IServiceProvider _services;

        internal Trailers(ClaimsPrincipal person)
        {
            _services = new ServiceCollection()
                .AddLogging()
                .AddAuthorizationCore()
                .AddSingleton<IUserProvider>(new OnePerson(person))
                .BuildServiceProvider();
            List = new TrailerList();
            _root = new RootErrorBoundary(new StubComponent(() => List));
            Html = Render();
        }

        internal TrailerList List { get; }

        internal string Html { get; private set; }

        internal int Removed => List.Removed;

        internal int SaidRemoved => List.SaidRemoved;

        internal int Archived => List.Archived;

        internal Task Click(string button) => Post(ClickOf(Html, button));

        internal async Task<bool> Post(string handlerId)
        {
            using var payload = JsonDocument.Parse("{}");
            try
            {
                return await _root.TryInvokeHandlerAsync(handlerId, payload.RootElement, _services);
            }
            finally
            {
                Html = Render();
            }
        }

        private string Render() => _root.RenderAsLiveRoot(_services);
    }

    internal sealed partial class TrailerList : Component
    {
        internal int Removed { get; private set; }

        internal int SaidRemoved { get; private set; }

        internal int Archived { get; private set; }

        protected override Component? Render() => Div[
            Ul[Removed > 0 ? null : Li["ABC-123"]],
            Button.Id("by-method-group").OnClick(Remove)["Remove"],
            Button.Id("by-lambda").OnClick(async () =>
            {
                await Remove();
                SaidRemoved++;
            })["Remove"],
            new Bin("through-a-component", Remove),
            Button.Id("signed-in-only").OnClick(Archive)["Archive"]
        ];

        [Authorize(Roles = "admin")]
        private Task Remove()
        {
            Removed++;
            return Task.CompletedTask;
        }

        [Authorize]
        private void Archive() => Archived++;
    }

    // A child that raises the callback it was handed, as a kit's button raises the page's OnClick.
    private sealed class Bin(string id, Func<Task> onClick) : Component
    {
        private readonly Callback _onClick = new(onClick);

        protected override Component? Render() => Button.Id(id).OnClick(() => _onClick.Invoke().AsTask())["Remove"];
    }

    private sealed class OnePerson(ClaimsPrincipal person) : IUserProvider
    {
        public ClaimsPrincipal Current => person;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
