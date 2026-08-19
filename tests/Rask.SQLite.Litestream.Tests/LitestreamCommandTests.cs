namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamCommandTests
{
    [Fact]
    public void Restore_builds_url_form_with_if_replica_exists()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db", ReplicaUrl = "s3://bucket/app" };

        var args = LitestreamCommand.Restore(options);

        Assert.Equal(["restore", "-if-replica-exists", "-o", "/data/app.db", "s3://bucket/app"], args);
    }

    [Fact]
    public void Restore_builds_config_form()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db", ConfigPath = "/etc/litestream.yml" };

        var args = LitestreamCommand.Restore(options);

        Assert.Equal(["restore", "-if-replica-exists", "-config", "/etc/litestream.yml", "/data/app.db"], args);
    }

    [Fact]
    public void Restore_writes_to_the_requested_output_path_in_url_form()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db", ReplicaUrl = "s3://bucket/app" };

        var args = LitestreamCommand.Restore(options, "/tmp/verify/verify.db", ifReplicaExists: false);

        // Verification restores somewhere else, and drops -if-replica-exists: a replica that isn't there
        // must be a failure, not a silent success.
        Assert.Equal(["restore", "-o", "/tmp/verify/verify.db", "s3://bucket/app"], args);
    }

    [Fact]
    public void Restore_writes_to_the_requested_output_path_in_config_form()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db", ConfigPath = "/etc/litestream.yml" };

        var args = LitestreamCommand.Restore(options, "/tmp/verify/verify.db", ifReplicaExists: false);

        // -config mode emitted no -o at all before, so a verification restore would have overwritten the
        // live database with a copy of itself. The positional path still selects which database to pull.
        Assert.Equal(
            ["restore", "-config", "/etc/litestream.yml", "-o", "/tmp/verify/verify.db", "/data/app.db"],
            args);
    }

    [Fact]
    public void Replicate_builds_url_form()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db", ReplicaUrl = "abs://container/app" };

        var args = LitestreamCommand.Replicate(options);

        Assert.Equal(["replicate", "/data/app.db", "abs://container/app"], args);
    }

    [Fact]
    public void Replicate_builds_config_form()
    {
        var options = new LitestreamOptions { ConfigPath = "/etc/litestream.yml" };

        var args = LitestreamCommand.Replicate(options);

        Assert.Equal(["replicate", "-config", "/etc/litestream.yml"], args);
    }
}
