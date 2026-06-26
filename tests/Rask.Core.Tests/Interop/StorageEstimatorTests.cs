using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class StorageEstimatorTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.storageSupported", true);

        Assert.True(await new StorageEstimator(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Estimate_ReturnsSnapshot_FromHelper()
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
    public void UsageRatio_IsZero_WhenQuotaUnknown()
    {
        Assert.Equal(0, new StorageEstimate(0, 0).UsageRatio);
    }

    [Fact]
    public async Task Estimate_ReturnsNull_WhenUnsupported()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new StorageEstimator(js).EstimateAsync());
    }
}
