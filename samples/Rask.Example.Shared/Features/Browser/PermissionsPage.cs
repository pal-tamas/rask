using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="PermissionsDemo" /> (<c>IPermissions</c>).</summary>
[Route("browser/permissions")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PermissionsPage : Component
{
    protected override RenderResult Head => Title()["Permissions — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Permissions",
            "Check a feature's permission state before triggering it via IPermissions (navigator.permissions)."),
        CodeSample(
            ["PermissionsDemo.cs"],
            Notes: "query() resolves a live PermissionStatus, so the call goes through __raskApi.permissionState, returning just the state string.",
            Result: PermissionsDemo())
    ];
}
