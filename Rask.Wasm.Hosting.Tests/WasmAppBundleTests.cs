using System.Reflection;

namespace Rask.Wasm.Hosting.Tests;

public class WasmAppBundleTests
{
    [Fact]
    public void ResolveFromAssembly_NullAssembly_ReturnsNull() =>
        Assert.Null(WasmAppBundle.ResolveFromAssembly(null));

    [Fact]
    public void ResolveFromAssembly_NoMetadataAttribute_ReturnsNull()
    {
        // System.Runtime has no Rask.WasmAppBundleDir metadata key.
        var sysRuntime = typeof(object).Assembly;
        Assert.Null(WasmAppBundle.ResolveFromAssembly(sysRuntime));
    }

    [Fact]
    public void ResolveFromAssembly_MetadataKeyPresent_ReturnsValue()
    {
        // The test csproj declares [assembly: AssemblyMetadata("Rask.WasmAppBundleDir", "/tmp/...")]
        // via an MSBuild <AssemblyMetadata> item.
        var resolved = WasmAppBundle.ResolveFromAssembly(typeof(WasmAppBundleTests).Assembly);
        Assert.Equal("/tmp/rask-wasm-hosting-tests-fake-bundle", resolved);
    }

    [Fact]
    public void ResolveFromAssembly_MetadataKeyWhitespace_ReturnsNull()
    {
        var assembly = new BuilderHelper().AssemblyWithMetadata("   ");
        Assert.Null(WasmAppBundle.ResolveFromAssembly(assembly));
    }

    private sealed class BuilderHelper
    {
        public Assembly AssemblyWithMetadata(string value)
        {
            var asmName = new AssemblyName("DynamicMetadataTest-" + Guid.NewGuid().ToString("N"));
            var asmBuilder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                asmName, System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var ctor = typeof(AssemblyMetadataAttribute)
                .GetConstructor(new[] { typeof(string), typeof(string) })!;
            asmBuilder.SetCustomAttribute(
                new System.Reflection.Emit.CustomAttributeBuilder(ctor, new object[] { WasmAppBundle.MetadataKey, value }));
            return asmBuilder;
        }
    }
}
