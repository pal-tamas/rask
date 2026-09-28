namespace Rask.Server.DevTools;

/// <summary>What a page's devtools script tag names: the host script, and the panel page it frames.</summary>
internal sealed record DevToolsPageTag(string ScriptUrl, string PanelUrl);
