// Builds Rask.Ui's script engines from npm's registry, with no npm and no Node, and follows their pins.
//
//   editor   src/Rask.Ui/Resources/editor   Tiptap behind Ui.Editor -> Resources/ui-editor.js
//   phone    src/Rask.Ui/Resources/phone    intl-tel-input behind Ui.Phone -> Resources/ui-phone.js, ui-phone-utils.js
//
// Each engine's bundle.json pins what it bundles; bundle-lock.json locks the whole tree (every package, its
// node_modules path, tarball and SHA-512). A build installs exactly the lock — each tarball checked against its
// digest, no install script run — and bundles the entry with the native esbuild the Rask build uses. The kit's own
// build never runs this: the bundles are committed. Run it after changing an engine's .ts or its bundle.json.
//
// Usage:  dotnet run --project scripts/tools/KitBundle -- build  editor|phone            # bundle what is locked
//         dotnet run --project scripts/tools/KitBundle -- lock   editor|phone            # re-lock bundle.json
//         dotnet run --project scripts/tools/KitBundle -- follow editor|phone [--check]  # move the pins, re-lock, build
//
// scripts/upstream/follow.sh runs `follow` every day (scripts/upstream/followers.json).

using KitBundle;

if (args.Length < 2 || args[0] is not ("build" or "lock" or "follow"))
{
    Console.Error.WriteLine("usage: KitBundle build|lock|follow editor|phone [--check]");
    return 2;
}

var following = args[0] == "follow";
var label = $"kit bundle {args[1]}";
try
{
    var engine = Engine.Named(args[1]);
    switch (args[0])
    {
        case "build":
            await engine.Build();
            return 0;
        case "lock":
            await engine.Lock();
            return 0;
        default:
            return await new Follower(engine).Run(check: args.Contains("--check"));
    }
}
catch (UnansweredException unanswered)
{
    // A registry that is down, slow or limiting is tomorrow's run for a follower; by hand, it is a failure.
    Console.Error.WriteLine($"{label}: {unanswered.Message}{(following ? "; leaving the files as they are." : ".")}");
    return following ? 0 : 1;
}
catch (KitBundleException failure)
{
    Console.Error.WriteLine($"{label}: {failure.Message}");
    return 1;
}
