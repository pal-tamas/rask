using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class PermissionsTests
{
    [Theory]
    [InlineData("granted", PermissionState.Granted)]
    [InlineData("denied", PermissionState.Denied)]
    [InlineData("prompt", PermissionState.Prompt)]
    [InlineData(null, PermissionState.Prompt)]
    public async Task A_query_uses_the_helper_and_maps_the_state(string? raw, PermissionState expected)
    {
        var js = new FakeJsRuntime();
        if (raw is not null)
        {
            js.SetResponse("__raskApi.permissionState", raw);
        }

        var permissions = new Permissions(js);

        Assert.Equal(expected, await permissions.QueryAsync(PermissionName.Geolocation));
    }

    [Theory]
    [InlineData(PermissionName.Geolocation, "geolocation")]
    [InlineData(PermissionName.ClipboardRead, "clipboard-read")]
    [InlineData(PermissionName.ClipboardWrite, "clipboard-write")]
    [InlineData(PermissionName.PersistentStorage, "persistent-storage")]
    [InlineData(PermissionName.Notifications, "notifications")]
    public async Task A_query_passes_the_spec_permission_name(PermissionName name, string specName)
    {
        var js = new FakeJsRuntime();
        var permissions = new Permissions(js);

        await permissions.QueryAsync(name);

        Assert.Equal([specName], js.ArgsFor("__raskApi.permissionState"));
    }
}
