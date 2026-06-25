using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class PermissionsTests
{
    [Theory]
    [InlineData("granted", PermissionState.Granted)]
    [InlineData("denied", PermissionState.Denied)]
    [InlineData("prompt", PermissionState.Prompt)]
    [InlineData(null, PermissionState.Prompt)]
    public async Task Query_UsesHelper_AndMapsState(string? raw, PermissionState expected)
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
    public async Task Query_PassesSpecPermissionName(PermissionName name, string specName)
    {
        var js = new FakeJsRuntime();
        var permissions = new Permissions(js);

        await permissions.QueryAsync(name);

        Assert.Equal([specName], js.ArgsFor("__raskApi.permissionState"));
    }
}
