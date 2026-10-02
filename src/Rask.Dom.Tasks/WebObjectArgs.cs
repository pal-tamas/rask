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
}
