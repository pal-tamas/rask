using System.Reflection.Metadata;
using Rask.Core.Live;

[assembly: MetadataUpdateHandler(typeof(ComponentHotReloadHandler))]

namespace Rask.Core.Live;

// The last link in Rask's `dotnet watch` story. Scoped CSS/JS already hot-reload via their own
// MetadataUpdateHandlers; this one handles COMPONENT CODE: when you edit a Render() (or anything it
// calls) and save, C# Hot Reload applies the new IL to the running process — but the live session,
// having no pending state change, wouldn't repaint until the next interaction. UpdateApplication closes
// that gap by re-rendering every active session, so component edits show up on save (the closest a
// compiled framework gets to Rails' no-build, edit-and-refresh loop).
//
// Invoked only under `dotnet watch` (hot reload is a debug-time feature; a normal/published run never
// calls MetadataUpdateHandlers), so it adds nothing to production.
internal static class ComponentHotReloadHandler
{
    // The runtime calls this after applying a metadata update. `updatedTypes` lists the changed types; we
    // deliberately re-render everything regardless (an edit to a helper/static a component calls wouldn't
    // appear there) — see LiveSessionBase.RerenderAllForHotReload.
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        LiveSessionBase.RerenderAllForHotReload(updatedTypes);
    }
}
