namespace Rask.Testing.Playwright.Tests;

public sealed class RaskSiteEnvironmentTests
{
    private const string App = "Rask__ConnectionStrings__App";
    private const string Logs = "Rask__ConnectionStrings__Logs";
    private static readonly Uri Address = new("http://localhost:5555/");

    [Fact]
    public async Task The_app_runs_as_a_test_over_a_database_in_the_runs_own_folder()
    {
        await using var site = new RaskSite<Program>();

        var start = site.StartInfo(Address);

        Assert.Equal("Testing", start.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal($"Data Source={Path.Combine(site.Folder, "app.db")}", start.Environment[App]);
        Assert.Equal($"Data Source={Path.Combine(site.Folder, "logs.db")}", start.Environment[Logs]);
    }

    [Fact]
    public async Task An_app_given_a_database_of_its_own_runs_over_it_and_keeps_the_run_s_log_store()
    {
        await using var site = new RaskSite<Program>();
        site.Environment[App] = "Server=sql;Database=ta_test";

        var start = site.StartInfo(Address);

        Assert.Equal("Server=sql;Database=ta_test", start.Environment[App]);
        Assert.Equal($"Data Source={Path.Combine(site.Folder, "logs.db")}", start.Environment[Logs]);
    }

    [Fact]
    public async Task A_connection_string_removed_in_the_environment_is_not_given_back_as_a_file()
    {
        await using var site = new RaskSite<Program>();
        site.Environment[Logs] = null;

        var start = site.StartInfo(Address);

        Assert.False(start.Environment.ContainsKey(Logs));
    }

    [Fact]
    public async Task The_environment_names_the_app_s_hosting_environment()
    {
        await using var site = new RaskSite<Program>();
        site.Environment["ASPNETCORE_ENVIRONMENT"] = "Staging";

        var start = site.StartInfo(Address);

        Assert.Equal("Staging", start.Environment["ASPNETCORE_ENVIRONMENT"]);
    }
}

/// <summary>Sets a variable of this process's own, which every site started meanwhile would inherit: run alone.</summary>
[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public sealed class ProcessEnvironment;

[Collection(nameof(ProcessEnvironment))]
public sealed class RaskSiteInheritedEnvironmentTests
{
    [Fact]
    public async Task A_connection_string_the_test_process_already_has_is_the_app_s()
    {
        await using var site = new RaskSite<Program>();
        Environment.SetEnvironmentVariable("Rask__ConnectionStrings__App", "Server=ci;Database=from_the_runner");

        try
        {
            var start = site.StartInfo(new Uri("http://localhost:5555/"));

            Assert.Equal("Server=ci;Database=from_the_runner", start.Environment["Rask__ConnectionStrings__App"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Rask__ConnectionStrings__App", null);
        }
    }
}
