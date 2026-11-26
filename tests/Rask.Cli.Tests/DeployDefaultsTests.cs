using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// The environment a deploy sets on a container by default gives way to the user's own value. Docker lets an
/// inline <c>-e</c> beat <c>--env-file</c>, so a default emitted next to the user's key would win whenever the user's
/// value travels in the env file — the default must not be emitted at all.
/// </summary>
public sealed class DeployDefaultsTests
{
    private const string Host = "deploy@box";
    private const string EnvFile = "/tmp/rask-shop.env";
    private const string SqlServer = "Rask__ConnectionStrings__App=Server=db;Database=shop;Encrypt=true";

    private static IReadOnlyList<string> Run(IReadOnlyList<string> env, string? envFilePath = null) =>
        DockerCommands.BuildRunArguments(Host, "shop", domain: null, color: null, 8080, env, envFilePath: envFilePath);

    private static int CountStartingWith(IReadOnlyList<string> args, string prefix) =>
        args.Count(a => a.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void A_user_app_connection_string_inline_replaces_the_default()
    {
        var env = new[] { SqlServer };

        var args = Run(env);

        Assert.DoesNotContain("Rask__ConnectionStrings__App=Data Source=/data/app.db", args, StringComparer.Ordinal);
        Assert.Equal(1, CountStartingWith(args, "Rask__ConnectionStrings__App="));
        Assert.Contains(SqlServer, args, StringComparer.Ordinal);
    }

    [Fact]
    public void A_user_app_connection_string_in_the_env_file_replaces_the_default()
    {
        var env = new[] { SqlServer };

        var args = Run(env, EnvFile);

        Assert.Equal(0, CountStartingWith(args, "Rask__ConnectionStrings__App="));
        Assert.Contains(EnvFile, args, StringComparer.Ordinal);
        Assert.Contains("shop-data:/data", args, StringComparer.Ordinal);
    }

    [Fact]
    public void Without_a_user_value_the_default_connection_strings_are_set()
    {
        var env = new[] { "OTHER=1" };

        var args = Run(env, EnvFile);

        Assert.Contains("Rask__ConnectionStrings__App=Data Source=/data/app.db", args, StringComparer.Ordinal);
        Assert.Contains("Rask__ConnectionStrings__Logs=Data Source=/data/logs.db", args, StringComparer.Ordinal);
    }

    [Fact]
    public void A_user_logs_connection_string_replaces_the_default_on_either_transport()
    {
        var env = new[] { "Rask__ConnectionStrings__Logs=Server=db;Database=logs" };

        var inline = Run(env);
        var viaFile = Run(env, EnvFile);

        Assert.Equal(1, CountStartingWith(inline, "Rask__ConnectionStrings__Logs="));
        Assert.Contains(env[0], inline, StringComparer.Ordinal);
        Assert.Equal(0, CountStartingWith(viaFile, "Rask__ConnectionStrings__Logs="));
        Assert.Contains("Rask__ConnectionStrings__App=Data Source=/data/app.db", viaFile, StringComparer.Ordinal);
    }

    [Fact]
    public void A_user_aspnetcore_environment_replaces_production()
    {
        var env = new[] { "ASPNETCORE_ENVIRONMENT=Staging" };

        var args = Run(env, EnvFile);

        Assert.Equal(0, CountStartingWith(args, "ASPNETCORE_ENVIRONMENT="));
        Assert.Contains("ASPNETCORE_SHUTDOWNTIMEOUTSECONDS=", string.Join(' ', args), StringComparison.Ordinal);
    }

    [Fact]
    public void A_colon_or_differently_cased_spelling_of_the_key_still_replaces_the_default()
    {
        var colon = new[] { "Rask:ConnectionStrings:App=Server=db" };
        var lower = new[] { "rask__connectionstrings__app=Server=db" };

        var fromColon = Run(colon, EnvFile);
        var fromLower = Run(lower, EnvFile);

        Assert.DoesNotContain("Rask__ConnectionStrings__App=Data Source=/data/app.db", fromColon, StringComparer.Ordinal);
        Assert.DoesNotContain("Rask__ConnectionStrings__App=Data Source=/data/app.db", fromLower, StringComparer.Ordinal);
    }
}
