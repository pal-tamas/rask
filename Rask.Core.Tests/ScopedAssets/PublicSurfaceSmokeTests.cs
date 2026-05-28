using System.Reflection;
using Rask.Core.ScopedAssets;

namespace Rask.Core.Tests.ScopedAssets;

/// <summary>
///     Smoke tests for the public surface of <see cref="ScopedAssetRegistry" /> and the
///     legacy bundle registries that continue to coexist during the additive migration.
///     If a consumer expects an API by name (per CLAUDE.md), it must be present and
///     callable; if a legacy API was meant to stay through this migration window, the
///     test asserts it's still resolvable. A future cleanup commit will flip the
///     legacy-API expectations to "removed" — those edits land alongside the deletions.
/// </summary>
public class PublicSurfaceSmokeTests
{
    [Theory]
    [InlineData(nameof(ScopedAssetRegistry.RegisterCss))]
    [InlineData(nameof(ScopedAssetRegistry.RegisterJs))]
    [InlineData(nameof(ScopedAssetRegistry.UnregisterCss))]
    [InlineData(nameof(ScopedAssetRegistry.UnregisterJs))]
    [InlineData(nameof(ScopedAssetRegistry.TryGetCss))]
    [InlineData(nameof(ScopedAssetRegistry.TryGetJs))]
    [InlineData(nameof(ScopedAssetRegistry.TryGetScopeId))]
    [InlineData(nameof(ScopedAssetRegistry.GetByHash))]
    [InlineData(nameof(ScopedAssetRegistry.InvalidateAll))]
    [InlineData(nameof(ScopedAssetRegistry.InvalidateAllCss))]
    [InlineData(nameof(ScopedAssetRegistry.InvalidateAllJs))]
    public void ScopedAssetRegistry_HasExpectedPublicMethod(string methodName)
    {
        var method = typeof(ScopedAssetRegistry).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public void ScopedAssetRegistry_HashHexLength_IsPublicConst_Twelve()
    {
        var field = typeof(ScopedAssetRegistry).GetField(
            nameof(ScopedAssetRegistry.HashHexLength),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field!.IsLiteral, "HashHexLength must be a const");
        Assert.Equal(12, (int)field.GetRawConstantValue()!);
    }

    [Fact]
    public void ScopedAssetRegistry_AssetChangedEvent_IsPublicStaticEvent()
    {
        var evt = typeof(ScopedAssetRegistry).GetEvent(
            nameof(ScopedAssetRegistry.AssetChanged),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(evt);
    }

    [Fact]
    public void AssetKind_HasCssAndJs_Values()
    {
        Assert.Equal(0, (int)AssetKind.Css);
        Assert.Equal(1, (int)AssetKind.Js);
    }

    [Fact]
    public void AssetBytes_RecordStruct_ExposesUtf8AndEtag()
    {
        var t = typeof(ScopedAssetRegistry.AssetBytes);
        Assert.True(t.IsValueType);
        Assert.NotNull(t.GetProperty("Utf8"));
        Assert.NotNull(t.GetProperty("Etag"));
    }
}
