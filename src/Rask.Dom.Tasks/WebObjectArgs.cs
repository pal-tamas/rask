using System;
using System.Collections.Generic;

namespace Rask.Core.Dom.Build;

// What an IDL `object` argument really takes, where the spec types it loosely: by Interface.member.argument, the
// dictionary MDN documents for it. The IDL never names these, so scripts/mdn/refresh.mjs reaches each one into the
// snapshot by hand (keep the two lists together), and the build fails on an entry the snapshot does not have.
internal static class WebObjectArgs
{
    public static readonly Dictionary<string, string> Dictionaries = new(StringComparer.Ordinal)
    {
        ["Permissions.query.permissionDesc"] = "PermissionDescriptor",
    };

    // …and where no dictionary can say it, the C# type: keyframes are CSS property names to values, any property at
    // all, one map per keyframe (MDN's "Keyframe formats"), each value a string or a number as in JavaScript —
    // `_box.Animate([new() { ["opacity"] = 0 }, new() { ["opacity"] = 1 }], 300)`.
    public static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        ["Element.animate.keyframes"] = Keyframes,
        ["KeyframeEffect.setKeyframes.keyframes"] = Keyframes,
    };

    private const string Keyframes = "global::System.Collections.Generic.Dictionary<string, object>[]";
}
