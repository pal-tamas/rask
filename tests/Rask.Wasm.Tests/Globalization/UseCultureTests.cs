using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;

namespace Rask.Wasm.Tests.Globalization;

// UseCulture is called by Program.cs AFTER the builder's constructor has registered the culture services, so
// what it says has to reach them anyway.
[Collection("RaskPrerenderEnvironment")]
public class UseCultureTests
{
    [Fact]
    public void Languages_named_after_the_builder_is_made_are_the_ones_the_app_runs_with()
    {
        RaskCulture.ResetForTests();
        var host = WasmHostBuilder.CreateDefault();

        try
        {
            host.UseCulture(c => c.SupportedCultures.Add("hu")).UseCulture(c => c.SupportedCultures.Add("en"));
            using var services = host.BuildServices();

            Assert.Equal(["hu", "en"], services.GetRequiredService<RaskCultureOptions>().SupportedCultures);
            Assert.Equal("hu", services.GetRequiredService<IRaskCulture>().Culture.Name);
            Assert.True(RaskCulture.IsEnabled);
        }
        finally
        {
            RaskCulture.ResetForTests();
        }
    }

    [Fact]
    public void An_app_that_names_no_language_leaves_culture_support_off()
    {
        RaskCulture.ResetForTests();

        using var services = WasmHostBuilder.CreateDefault().BuildServices();

        Assert.Empty(services.GetRequiredService<RaskCultureOptions>().SupportedCultures);
        Assert.False(RaskCulture.IsEnabled);
    }
}
