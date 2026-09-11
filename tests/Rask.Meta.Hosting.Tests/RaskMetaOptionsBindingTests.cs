using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Rask.Meta.Hosting.Tests;

// MetaHostingOptions come from the build's metadata, then Rask:Meta, then the AddRaskMeta callback, then a rask dev
// session. Rask:Meta is read key by key — the Framework preset cannot be built by the binder — so these tests are what
// notice a property that never reads configuration.
public sealed class RaskMetaOptionsBindingTests
{
    private static readonly Dictionary<string, string?> EverySetting = new()
    {
        // The same names the build's RaskMetaFramework property takes.
        ["Rask:Meta:Framework"] = "nuxt",
        ["Rask:Meta:AppDirectory"] = "web",
        ["Rask:Meta:NodeExecutable"] = "/usr/bin/node",
        ["Rask:Meta:Port"] = "4100",
        ["Rask:Meta:StartupTimeout"] = "00:00:45",
        ["Rask:Meta:ShutdownTimeout"] = "00:00:07",
        ["Rask:Meta:MaxRestartAttempts"] = "9",
        ["Rask:Meta:HealthyRunThreshold"] = "00:02:00",
        ["Rask:Meta:BaseUrl"] = "http://127.0.0.1:8080",
        ["Rask:Meta:BaseUrlVariable"] = "APP_BASE_URL",
        ["Rask:Meta:SuperviseNode"] = "false",
        ["Rask:Meta:Environment:NODE_OPTIONS"] = "--max-old-space-size=512",
    };

    [Fact]
    public void Every_setting_binds_from_the_Rask_Meta_section()
    {
        var options = Bind(EverySetting);

        Assert.Same(MetaFramework.Nuxt, options.Framework);
        Assert.Equal("web", options.AppDirectory);
        Assert.Equal("/usr/bin/node", options.NodeExecutable);
        Assert.Equal(4100, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(45), options.StartupTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), options.ShutdownTimeout);
        Assert.Equal(9, options.MaxRestartAttempts);
        Assert.Equal(TimeSpan.FromMinutes(2), options.HealthyRunThreshold);
        Assert.Equal("http://127.0.0.1:8080", options.BaseUrl);
        Assert.Equal("APP_BASE_URL", options.BaseUrlVariable);
        Assert.False(options.SuperviseNode);
        Assert.Equal("--max-old-space-size=512", options.Environment["NODE_OPTIONS"]);
    }

    [Fact]
    public void Every_property_is_covered_by_the_binding_test()
    {
        // Read key by key, so a new property is silently never configurable until it is added to BindSection —
        // and to EverySetting above, which is what this pins.
        var properties = typeof(MetaHostingOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);
        var covered = EverySetting.Keys
            .Select(k => k["Rask:Meta:".Length..].Split(':')[0])
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(covered, properties);
    }

    [Fact]
    public void An_absent_section_leaves_the_defaults()
    {
        var options = Bind([]);

        Assert.Same(MetaFramework.TanStackStart, options.Framework);
        Assert.Equal(3000, options.Port);
        Assert.True(options.SuperviseNode);
    }

    [Fact]
    public void A_framework_Rask_does_not_host_is_refused_by_name()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bind(new() { ["Rask:Meta:Framework"] = "Gatsby" }));

        Assert.Contains("Gatsby", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dev_session_still_wins_over_the_section()
    {
        var options = Bind(new() { ["Rask:Meta:Port"] = "4100", ["Rask:Meta:SuperviseNode"] = "true" });

        RaskMetaServiceCollectionExtensions.ApplyDevServer(options, _ => "http://localhost:5173");

        Assert.Equal(5173, options.Port);
        Assert.False(options.SuperviseNode);
    }

    private static MetaHostingOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new MetaHostingOptions();
        RaskMetaServiceCollectionExtensions.BindSection(configuration.GetSection("Rask:Meta"), options);
        return options;
    }
}
