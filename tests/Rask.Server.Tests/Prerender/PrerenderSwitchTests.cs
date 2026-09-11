using System.Reflection;
using System.Reflection.Emit;
using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// The build records <RaskPrerender> as assembly metadata, and AddRask reads it back off the entry
// assembly. These pin the reading half against assemblies that carry exactly what the build writes.
public class PrerenderSwitchTests
{
    [Fact]
    public void AnAssemblyWithoutTheMetadata_IsOff()
    {
        // Every existing app, and the test runner every existing test runs under: nobody gets a page cache
        // they did not ask for.
        Assert.False(PrerenderSwitch.IsOn(typeof(PrerenderSwitchTests).Assembly));
    }

    [Fact]
    public void NoEntryAssembly_IsOff()
    {
        // Assembly.GetEntryAssembly() is null under some hosts (a native one embedding the runtime).
        Assert.False(PrerenderSwitch.IsOn(null));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)] // MSBuild property values are case-insensitive, so the build may write either
    [InlineData("false", false)]
    [InlineData("", false)]
    public void TheValueTheBuildWrote_Decides(string value, bool expected)
    {
        Assert.Equal(expected, PrerenderSwitch.IsOn(AssemblyWith(PrerenderSwitch.MetadataKey, value)));
    }

    [Fact]
    public void AnotherKey_IsNotTheSwitch()
    {
        Assert.False(PrerenderSwitch.IsOn(AssemblyWith("Rask.WasmAppBundleDir", "true")));
    }

    private static Assembly AssemblyWith(string key, string value)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PrerenderSwitchFixture" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
            [key, value]));
        return builder;
    }
}
