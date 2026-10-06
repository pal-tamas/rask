using System.Text.Json;
using Rask.Core.Routing;
using Rask.External.Generated;
using Rask.TestSupport;

namespace Rask.External.Tests
{
    [Route("/")]
    public sealed partial class FrontPage : Component;

    [Route("/login")]
    public sealed partial class LoginPage : Component;

    [Route("/users/{id:int}")]
    public sealed partial class UserPage : Component
    {
        [RouteParam] public int Id { get; set; }

        [QueryParam] public string? Tab { get; set; }
    }

    [Route("/files/{name}/{version?}")]
    public sealed partial class FilePage : Component
    {
        [RouteParam] public string Name { get; set; } = string.Empty;

        [RouteParam] public string? Version { get; set; }
    }

    [Route("/orders/{id:guid}")]
    public sealed partial class OrderPage : Component
    {
        [RouteParam] public Guid Id { get; set; }

        [QueryParam] public bool? Paid { get; set; }

        [QueryParam("sort by")] public string? Order { get; set; }

        [QueryParam] public int Page { get; set; }
    }

    [Route("/days/{day}")]
    public sealed partial class DayPage : Component
    {
        [RouteParam] public DateOnly Day { get; set; }

        [QueryParam] public TimeOnly? At { get; set; }

        [QueryParam] public DateTime? Since { get; set; }

        [QueryParam] public double? Ratio { get; set; }

        [QueryParam] public long? Count { get; set; }
    }

    // The front-end half of `@rask/routes`, run for real: IslandRoutesFixture.ts imports the module the BUILD
    // generated from the pages above and prints what it formats and what it asks the host to do. Each URL is
    // compared with the one the C# `Routes` class formats for the same page and the same values, because "the
    // same call in TypeScript" is only true if it lands on the same address.
    public sealed class IslandRoutesTests
    {
        private static readonly Guid Order = Guid.Parse("0b6f3a52-9d1c-4a7e-8f20-3c5d7e9a1b42");

        [Fact]
        public void Go_navigates_to_the_same_url_the_csharp_route_formats()
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["front"] = Routes.FrontPage(),
                ["int"] = Routes.UserPage(42),
                ["negative int"] = Routes.UserPage(-7),
                ["query text"] = Routes.UserPage(42, Tab: "billing & invoices"),
                ["query left out"] = Routes.UserPage(42, Tab: null),
                ["text needing encoding"] = Routes.FilePage("a b/c?d#e&f=g+h"),
                ["text encodeURIComponent lets through"] = Routes.FilePage("it's (a) *test*!~"),
                ["non-ascii text"] = Routes.FilePage("árvíztűrő 日本 😀"),
                ["optional segment"] = Routes.FilePage("report", "v2"),
                ["optional segment left out"] = Routes.FilePage("report"),
                ["guid"] = Routes.OrderPage(Order),
                ["bool true"] = Routes.OrderPage(Order, Paid: true),
                ["bool false"] = Routes.OrderPage(Order, Paid: false),
                ["text under an encoded key"] = Routes.OrderPage(Order, Order: "oldest first"),
                ["non-nullable query is always written"] = Routes.OrderPage(Order, Page: 3),
                ["every query at once"] = Routes.OrderPage(Order, true, "newest", 2),
                ["date"] = Routes.DayPage(new DateOnly(2026, 10, 6)),
                ["time"] = Routes.DayPage(new DateOnly(2026, 1, 9), At: new TimeOnly(13, 5, 9)),
                ["date and time"] = Routes.DayPage(new DateOnly(2026, 1, 9), Since: new DateTime(2026, 3, 4, 5, 6, 7)),
                ["fraction"] = Routes.DayPage(new DateOnly(2026, 1, 9), Ratio: 0.25),
                ["long"] = Routes.DayPage(new DateOnly(2026, 1, 9), Count: 9007199254740991),
                ["nested on a name clash"] = Routes.Admin.HomePage(),
                ["nested on a name clash, the other"] = Routes.Shop.HomePage(),
            };

            var doc = NodeFixture.Run("IslandRoutesFixture");
            if (doc is null)
            {
                return; // no node here
            }

            var urls = Strings(doc.Value.GetProperty("urls"));
            var navigated = Strings(doc.Value.GetProperty("navigated"));
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), urls.Keys.Order(StringComparer.Ordinal));
            Assert.All(expected, pair => Assert.Equal(pair.Value, urls[pair.Key]));
            Assert.All(expected, pair => Assert.Equal(pair.Value, navigated[pair.Key]));
        }

        [Fact]
        public void Replacing_asks_the_host_to_replace_the_history_entry()
        {
            var doc = NodeFixture.Run("IslandRoutesFixture");
            if (doc is null)
            {
                return; // no node here
            }

            var calls = doc.Value.GetProperty("history");

            Assert.Equal("""{"url":"/login","replace":false}""", calls.GetProperty("go").GetRawText());
            Assert.Equal("""{"url":"/login","replace":true}""", calls.GetProperty("replacing").GetRawText());
            Assert.Equal("""{"url":"/users/42","replace":false}""", calls.GetProperty("goTo").GetRawText());
            Assert.Equal("""{"url":"/users/42","replace":true}""", calls.GetProperty("goToReplacing").GetRawText());
        }

        [Fact]
        public void Go_With_changes_one_query_key_and_keeps_the_path()
        {
            var doc = NodeFixture.Run("IslandRoutesFixture");
            if (doc is null)
            {
                return; // no node here
            }

            var query = doc.Value.GetProperty("query");

            // Navigator.SetQuery: the path is untouched, the key keeps the spelling it already had, the other
            // keys stay, a null removes — and it is a new history entry, as Go.With is in C#.
            Assert.Equal("""{"url":"/app/list?page=1&Sort=desc","replace":false}""", query.GetProperty("with").GetRawText());
            Assert.Equal("""{"url":"/app/list?page=1&Sort=asc&q=a%20b%26c","replace":false}""", query.GetProperty("withNew").GetRawText());
            Assert.Equal("""{"url":"/app/list?Sort=asc","replace":false}""", query.GetProperty("withNull").GetRawText());
        }

        [Fact]
        public void Go_Without_removes_a_key_case_insensitively()
        {
            var doc = NodeFixture.Run("IslandRoutesFixture");
            if (doc is null)
            {
                return; // no node here
            }

            var query = doc.Value.GetProperty("query");

            Assert.Equal("""{"url":"/app/list?page=1","replace":false}""", query.GetProperty("without").GetRawText());
            Assert.Equal("""{"url":"/app/list?page=1&Sort=asc","replace":false}""", query.GetProperty("withoutMissing").GetRawText());
            Assert.Equal("""{"url":"/app/list","replace":false}""", query.GetProperty("withoutAll").GetRawText());
        }

        [Fact]
        public void A_link_carries_the_path_base_and_the_in_app_marker()
        {
            var doc = NodeFixture.Run("IslandRoutesFixture");
            if (doc is null)
            {
                return; // no node here
            }

            var based = doc.Value.GetProperty("based");

            // NavLink writes LiveOptions.PathBase + the route; here the base is the directory of <base href="/docs/">.
            var href = "/docs" + Routes.UserPage(42, Tab: "a b");
            Assert.Equal(JsonSerializer.Serialize(new Dictionary<string, string> { ["href"] = href, ["data-rask-nav"] = "" }),
                based.GetProperty("link").GetRawText());
            Assert.Equal(href, based.GetProperty("url").GetString());
            Assert.Equal("/docs/", based.GetProperty("front").GetString());
        }

        [Fact]
        public void A_call_before_the_host_is_ready_says_so()
        {
            var doc = NodeFixture.Run("IslandRoutesFixture");
            if (doc is null)
            {
                return; // no node here
            }

            var errors = doc.Value.GetProperty("early").EnumerateArray().Select(e => e.GetString()!).ToArray();

            Assert.Equal(2, errors.Length);
            Assert.StartsWith("Rask islands: Go() asked for \"/login\" before the Rask runtime was ready", errors[0], StringComparison.Ordinal);
            Assert.StartsWith("Rask islands: Go.With asked for \"/app/list?page=2", errors[1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task Go_To_accepts_only_a_generated_route()
        {
            using var project = new TempProject();
            project.Write("routes.ts", RaskExternalRoutes.TypeScript);
            project.Write("good.ts",
                """
                import { Routes, Go } from './routes'
                Go.To(Routes.UserPage({ Id: 42, Tab: 'billing' })).Replacing()
                Routes.FilePage({ Name: 'report' }).Go()
                Routes.OrderPage({ Id: '0b6f3a52-9d1c-4a7e-8f20-3c5d7e9a1b42', Paid: true })
                export const link: { href: string } = Routes.FrontPage().Link
                """);
            project.Write("bad.ts",
                """
                import { Routes, Go } from './routes'
                Go.To('/users/42')
                Go.To({ Url: '/x', Link: { href: '/x', 'data-rask-nav': '' }, Go: () => ({ Replacing() {} }) })
                Routes.UserPage({ id: 42 })
                Routes.UserPage({ Id: '42' })
                Routes.UserPage()
                Routes.FrontPage({ Id: 1 })
                Routes.OrderPage({ Id: 'x', Paid: 'yes' })
                """);
            const string options = " --noEmit --pretty false --strict --noUnusedLocals --target esnext --module preserve"
                                   + " --moduleResolution bundler --lib esnext,dom";
            var tsgo = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "node-fixtures", "tsgo.path")).Trim();

            var good = await TestProcess.Run(tsgo, "good.ts" + options, project.Path("."), cancellationToken: TestContext.Current.CancellationToken);
            var bad = await TestProcess.Run(tsgo, "bad.ts" + options, project.Path("."), cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(good.ExitCode == 0, good.Output);
            Assert.NotEqual(0, bad.ExitCode);
            var reported = Enumerable.Range(2, 7).Where(line => bad.Output.Contains($"bad.ts({line},", StringComparison.Ordinal));
            Assert.True(reported.SequenceEqual(Enumerable.Range(2, 7)), bad.Output);
            Assert.DoesNotContain("routes.ts(", bad.Output, StringComparison.Ordinal);
        }

        private static Dictionary<string, string> Strings(JsonElement map) =>
            map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);

        private sealed class TempProject : IDisposable
        {
            private readonly string _root = Directory.CreateTempSubdirectory("rask-island-routes").FullName;

            public string Path(string file) => System.IO.Path.Combine(_root, file);

            public void Write(string file, string content) => File.WriteAllText(Path(file), content);

            public void Dispose() => Directory.Delete(_root, recursive: true);
        }
    }
}

namespace Rask.External.Tests.Admin
{
    // [SkipFactory]: two components sharing a simple name get no chain entry, and these are only ever routed to.
    [Route("/admin")]
    [SkipFactory]
    public sealed partial class HomePage : Component;
}

namespace Rask.External.Tests.Shop
{
    [Route("/shop")]
    [SkipFactory]
    public sealed partial class HomePage : Component;
}
