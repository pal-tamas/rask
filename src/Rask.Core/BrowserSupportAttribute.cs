namespace Rask.Core;

// The first version of each engine that ships a generated web API member, or an attribute's keyword (`Popover.Hint`),
// from MDN's browser-compat-data. Written by the MDN emitter beside the doc comment's support line and read by the targets
// analyzer (RASK098) off the symbol; an engine left unset does not ship the member. Nothing reads it at run time:
// IsSupported asks the browser itself.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
internal sealed class BrowserSupportAttribute : Attribute
{
    public string? Chrome { get; set; }

    public string? Firefox { get; set; }

    public string? Safari { get; set; }
}
