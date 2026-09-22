using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class StorageEstimatorTests
{
    [Fact]
    public async Task Asking_whether_storage_estimation_is_supported_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.storageSupported", true);

        Assert.True(await new StorageEstimator(js).IsSupportedAsync());
    }

    [Fact]
    public async Task An_estimate_gives_the_snapshot_from_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.storageEstimate", new StorageEstimate(1_000_000, 250_000));

        var estimate = await new StorageEstimator(js).EstimateAsync();

        Assert.NotNull(estimate);
        Assert.Equal(1_000_000, estimate!.Quota);
        Assert.Equal(250_000, estimate.Usage);
        Assert.Equal(0.25, estimate.UsageRatio);
    }

    [Fact]
    public void The_usage_ratio_is_zero_when_the_quota_is_unknown()
    {
        Assert.Equal(0, new StorageEstimate(0, 0).UsageRatio);
    }

    [Fact]
    public async Task An_estimate_is_null_when_unsupported()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new StorageEstimator(js).EstimateAsync());
    }

    [Fact]
    public async Task Asking_whether_storage_is_persisted_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.storagePersisted", true);

        Assert.True(await new StorageEstimator(js).IsPersistedAsync());
    }

    [Fact]
    public async Task Requesting_persistence_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.storagePersist", true);

        Assert.True(await new StorageEstimator(js).RequestPersistAsync());
    }

    // The helper resolves false rather than throwing where navigator.storage.persist is absent, so an app
    // can treat "not persisted" and "can't be persisted" the same way: writes are evictable either way.
    [Fact]
    public async Task Persistence_reports_false_when_unsupported()
    {
        var js = new FakeJsRuntime();

        Assert.False(await new StorageEstimator(js).IsPersistedAsync());
        Assert.False(await new StorageEstimator(js).RequestPersistAsync());
    }
}
