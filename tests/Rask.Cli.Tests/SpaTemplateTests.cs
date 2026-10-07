using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     What <c>rask new --template react</c> writes, and what it deliberately does not: the decisions a
///     compile cannot see.
/// </summary>
public sealed class SpaTemplateTests
{
    private const string Root = "/tmp/spa";

    private static ScaffoldResult Generate(ServerBatteries? batteries = null, SpaFramework? framework = null) =>
        ProjectGenerator.GenerateSpa(Root, "Shop", framework ?? SpaFramework.React, batteries ?? new ServerBatteries(), "1.2.3");

    private static string Content(ScaffoldResult result, string endsWith) =>
        result.Files.Single(f => f.Path.Replace('\\', '/').EndsWith(endsWith, StringComparison.Ordinal)).Content;

    private static bool Has(ScaffoldResult result, string endsWith) =>
        result.Files.Any(f => f.Path.Replace('\\', '/').EndsWith(endsWith, StringComparison.Ordinal));

    private static IEnumerable<ScaffoldFile> Client(ScaffoldResult result) =>
        result.Files.Where(f => f.Path.Replace('\\', '/').Contains("/client/", StringComparison.Ordinal));

    /// <summary>The client's global stylesheet, whatever its framework calls it: the one that imports Tailwind.</summary>
    private static string Stylesheet(ScaffoldResult result) =>
        Client(result)
            .Where(f => f.Path.EndsWith(".css", StringComparison.Ordinal))
            .Select(f => f.Content)
            .Single(css => Regex.IsMatch(css, "@import ['\"]tailwindcss['\"];"));

    public static TheoryData<string> Frameworks() => [.. SpaFramework.All.Select(framework => framework.Key)];

    private static SpaFramework Framework(string key)
    {
        Assert.True(SpaFramework.TryGet(key, out var framework));
        return framework;
    }

    // The last thing `rask new` prints is the first thing anyone follows, and nothing else reads it.
    [Fact]
    public void Next_steps_name_directories_the_scaffold_produced()
    {
        var result = Generate();

        var notes = result.Notes ?? string.Empty;

        Assert.Contains("Shop/client/src/rask/", notes, StringComparison.Ordinal);
        Assert.NotEmpty(Client(result));
    }

    // Reflection over the declared fields, which catches the framework somebody adds and forgets to list.
    [Fact]
    public void Every_declared_framework_is_in_the_list_the_catalog_reads()
    {
        var declared = typeof(SpaFramework)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SpaFramework))
            .Select(field => (SpaFramework)field.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(declared);
        Assert.All(declared, framework => Assert.Contains(framework, SpaFramework.All));
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_is_offered_as_a_template_with_a_committed_tree(string key)
    {
        var found = TemplateCatalog.TryGet(key, out var template);

        Assert.True(found);
        Assert.Equal(key, template.Key);
        Assert.True(TemplateMaterializer.Has(key), $"SpaFramework lists '{key}' but src/Rask.Templates/{key}/ is not embedded.");
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_scaffolds_something_that_can_dispatch(string key)
    {
        var result = Generate(framework: Framework(key));

        var client = string.Join("\n", Client(result).Select(f => f.Content));

        // Both directions, not just the read: a command goes back the same way a query came.
        Assert.Contains("rask/messages", client, StringComparison.Ordinal);
        Assert.Contains("rask/client", client, StringComparison.Ordinal);
        Assert.Contains("getGreeting", client, StringComparison.Ordinal);
        Assert.Contains("recordVisit", client, StringComparison.Ordinal);
        Assert.Contains("rask.dispatch(", client, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dev_server_proxies_the_wire_to_the_host()
    {
        var config = Content(Generate(), "/client/vite.config.ts");

        // The browser talks to Vite and Vite forwards /_rask, so the browser only ever sees one origin.
        Assert.Contains("'/_rask'", config, StringComparison.Ordinal);
        Assert.Contains("target: 'http://localhost:5000'", config, StringComparison.Ordinal);
    }

    // One decision written in two files; a mismatch is a dev session where every call 502s. Plain http only:
    // the proxy would have to follow the host's redirect to an https port otherwise.
    [Fact]
    public void The_host_listens_where_the_proxy_points()
    {
        var launch = Content(Generate(), "/Properties/launchSettings.json");

        Assert.Contains("\"applicationUrl\": \"http://localhost:5000\"", launch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_names_the_dev_server_rask_dev_opens()
    {
        var csproj = Content(Generate(), "/Shop.csproj");

        Assert.Contains($"<RaskSpaDevServerUrl>{LocalDevServers.Vite}</RaskSpaDevServerUrl>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.Spa.Hosting\" Version=\"1.2.3\"/>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("<RaskSpaDistDir>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_is_served_by_RaskApp()
    {
        var program = Content(Generate(), "/Program.cs");

        // Every endpoint ahead of the fallback to index.html is Serve()'s order, so the scaffold writes no Map call.
        Assert.Contains("RaskApp.Create(args);", program, StringComparison.Ordinal);
        Assert.EndsWith("app.Serve();\n", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapRaskSpa", program, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_rask_new_keeps_the_committed_Program_with_every_battery_on()
    {
        Assert.True(TemplateCatalog.TryGet("react", out var template));

        var program = Content(Generate(BatterySelection.ToBatteries(template, [])), "/Program.cs");

        Assert.DoesNotContain(".Off();", program, StringComparison.Ordinal);
        Assert.Contains("using Shop.Features.Hello;", program, StringComparison.Ordinal);
        Assert.Contains("app.Services.AddSingleton<VisitCounter>();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void A_battery_turned_off_is_a_line_ahead_of_the_apps_own_services()
    {
        Assert.True(TemplateCatalog.TryGet("react", out var template));

        var program = Content(Generate(BatterySelection.ToBatteries(template, ["pwa"])), "/Program.cs");

        // The PWA is the client's own manifest and worker, so what the host turns off without it is push.
        Assert.Contains("app.Configure(c => c.Push.Off());", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("c.Push.Off()", StringComparison.Ordinal)
            < program.IndexOf("AddSingleton<VisitCounter>", StringComparison.Ordinal));
        Assert.Contains("using Shop.Features.Hello;", program, StringComparison.Ordinal);
    }

    // A DateTime of unspecified kind is parsed by a browser as LOCAL time; the starter teaches the offset.
    [Fact]
    public void The_greeting_carries_an_offset_rather_than_a_bare_DateTime()
    {
        var messages = Content(Generate(), "/Features/Hello/Messages.cs");

        Assert.Contains("DateTimeOffset SeenAt", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime SeenAt", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void Cqrs_is_on_whether_or_not_it_was_asked_for()
    {
        var program = Content(Generate(), "/Program.cs");

        Assert.DoesNotContain("Cqrs.Off()", program, StringComparison.Ordinal);
        Assert.Contains("c.Data.Off();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_supports_the_batteries_its_host_and_client_carry()
    {
        Assert.True(TemplateCatalog.TryGet("react", out var template));

        var flags = template.SupportedFlags;

        Assert.Superset(new HashSet<string> { "pwa", "push", "data", "docker", "ops", "cqrs" }, flags.ToHashSet());
        Assert.DoesNotContain("tests", flags);
        Assert.DoesNotContain("auth", flags);
    }

    [Fact]
    public void A_database_is_the_apps_own_with_no_context_to_write()
    {
        var result = Generate(new ServerBatteries { Data = true });

        // RaskApp brings the context; the app declares only its user, which is what switches accounts on.
        Assert.False(Has(result, "/Features/Shared/AppDbContext.cs"));
        Assert.True(Has(result, "/Features/Shared/User.cs"));
        Assert.DoesNotContain("c.Data.Off()", Content(result, "/Program.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Tailwind_is_imported_by_the_clients_own_stylesheet_with_no_config_file(string key)
    {
        var result = Generate(framework: Framework(key));

        var sheet = Stylesheet(result);

        // v4 needs no config file and no content array: it detects the sources itself.
        Assert.DoesNotContain("content:", sheet, StringComparison.Ordinal);
        Assert.False(Has(result, "/client/tailwind.config.js"));
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_starter_stylesheet_compiles_daisyui_beneath_the_utilities(string key)
    {
        var sheet = Stylesheet(Generate(framework: Framework(key)));

        // From node_modules, by name. Without the layer statement daisyUI outranks the utilities beside it
        // and `class="btn px-8"` ignores the px-8.
        Assert.Contains("@plugin \"daisyui\";", sheet, StringComparison.Ordinal);
        Assert.Contains("@layer properties, theme, base, components, daisyui, utilities;", sheet, StringComparison.Ordinal);
        Assert.Contains("@apply bg-base-200", sheet, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Pwa_writes_the_manifest_the_icon_and_the_service_worker_into_the_bundle_root(string key)
    {
        var result = Generate(new ServerBatteries { Pwa = true }, Framework(key));

        // public/ because every bundler copies it verbatim to the bundle root, in a build AND under the dev server.
        Assert.True(Has(result, "/client/public/manifest.webmanifest"));
        Assert.True(Has(result, "/client/public/icon.svg"));
        var worker = Content(result, "/client/public/rask-sw.js");
        Assert.Matches("addEventListener\\(['\"]push['\"]", worker);
        Assert.Contains("notificationclick", worker, StringComparison.Ordinal);

        // No app-shell cache: the bundler fingerprints its assets, so a cached shell points at files that are gone.
        Assert.DoesNotContain("caches.open", worker, StringComparison.Ordinal);
        Assert.Contains("rask-sw.js", string.Join("\n", Client(result).Where(f => f.Path.EndsWith(".html", StringComparison.Ordinal)).Select(f => f.Content)), StringComparison.Ordinal);
    }

    [Fact]
    public void Push_subscribes_through_the_browsers_own_api_and_the_hosts_endpoints()
    {
        var result = Generate(new ServerBatteries { Push = true }.Normalized());

        // src/push.ts, not src/rask/: which endpoints and when to ask are the developer's, so it is a committed file.
        var client = Content(result, "/client/src/push.ts");

        Assert.Contains("fetch('/_rask/push/key')", client, StringComparison.Ordinal);
        Assert.Contains("'/_rask/push/subscribe', subscription.toJSON()", client, StringComparison.Ordinal);
        Assert.Contains("'/_rask/push/unsubscribe'", client, StringComparison.Ordinal);
        Assert.Contains("pushManager.subscribe(", client, StringComparison.Ordinal);
        Assert.Contains("userVisibleOnly: true", client, StringComparison.Ordinal);

        // No Rask module: the browser layer that used to wrap this is gone, and src/rask/browser/ ships auth alone.
        Assert.DoesNotContain("import ", client, StringComparison.Ordinal);

        // The host half is the Push battery's own endpoints, which Serve() maps.
        Assert.DoesNotContain("Push.Off()", Content(result, "/Program.cs"), StringComparison.Ordinal);
        Assert.Contains("\"Push\": {", Content(result, "/appsettings.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_pwa_no_client_carries_a_service_worker_or_a_manifest_link()
    {
        var result = Generate();

        Assert.False(Has(result, "/client/public/rask-sw.js"));
        Assert.False(Has(result, "/client/public/manifest.webmanifest"));
        Assert.False(Has(result, "/client/src/push.ts"));
        var page = Content(result, "/client/index.html");
        Assert.DoesNotContain("manifest", page, StringComparison.Ordinal);
        Assert.DoesNotContain("serviceWorker", page, StringComparison.Ordinal);
        Assert.DoesNotContain("rask:if", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sign_in_screen_calls_the_auth_module_the_build_writes()
    {
        var result = Generate(new ServerBatteries { Data = true });

        var screen = Content(result, "/client/src/Auth.tsx");

        // src/rask/browser/auth.ts is what Rask.Spa.Hosting copies beside the typed client on every build.
        Assert.Contains("from './rask/browser/auth'", screen, StringComparison.Ordinal);
        Assert.Contains("'/login'", Content(result, "/client/src/main.tsx"), StringComparison.Ordinal);
        Assert.Contains("'/register'", Content(result, "/client/src/main.tsx"), StringComparison.Ordinal);
    }

    // The image is built from the project directory, so every path in it is relative to that.
    [Fact]
    public void The_docker_image_builds_from_the_project_directory()
    {
        var dockerfile = Content(Generate(new ServerBatteries { Docker = true }), "/Dockerfile");

        Assert.Contains("COPY [\"Shop.csproj\", \"./\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY [\"client/package.json\", \"client/package-lock.json\", \"client/\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("npm ci", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("Shop/", dockerfile, StringComparison.Ordinal);
    }

    // A floor the image's own `dotnet publish` refuses is a broken template; a line behind the LTS is a slow one.
    [Fact]
    public void The_docker_image_installs_the_current_node_lts()
    {
        var dockerfile = Content(Generate(new ServerBatteries { Docker = true }), "/Dockerfile");
        var floor = Version.Parse(Regex.Match(
            RepoPins.Text("src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.props"),
            @"<RaskSpaMinimumNode[^>]*>([0-9.]+)</RaskSpaMinimumNode>").Groups[1].Value);

        var installed = Regex.Match(dockerfile, @"deb\.nodesource\.com/setup_(\d+)\.x");

        Assert.True(installed.Success, $"the SPA Dockerfile no longer installs Node from NodeSource:\n{dockerfile}");
        var major = int.Parse(installed.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(major >= floor.Major, $"the image installs Node {major}.x, which the build's own floor ({floor}) refuses.");
        Assert.Equal(NodeRequirement.ScaffoldLine.Major, major);
    }
}
