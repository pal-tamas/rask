using System.Security.Cryptography;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Rask.TestSupport.ScriptFixtures;

/// <summary>
///     The <c>__raskFixture</c> object a fixture's engine sees: what <c>fixture.ts</c> and <c>web-globals.js</c> call
///     to reach .NET — the arguments, the report, the console sink, the timers, a file read, in-memory modules.
/// </summary>
internal static class FixtureHost
{
    public static JsObject Create(
        Engine engine, IReadOnlyList<string> arguments, FixtureOutcome outcome, FixtureTimers timers, FixtureModuleLoader modules)
    {
        var host = new JsObject(engine);
        host.Set("args", engine.Intrinsics.Array.Construct(arguments.Select(a => (JsValue)a).ToArray()));

        Add(engine, host, "report", a => outcome.Reported(a.At(0).AsString())
            ? JsValue.Undefined
            : throw new JavaScriptException(engine.Intrinsics.Error, "report(...) was called twice; a fixture reports once."));
        Add(engine, host, "print", a => Done(() => outcome.Printed(a.At(0).AsString(), a.At(1).AsString())));
        Add(engine, host, "uncaught", a => Done(() => outcome.Uncaught(a.At(0))));
        Add(engine, host, "settled", a => Done(() => outcome.Settled(a.Length > 0 ? a[0] : null)));

        Add(engine, host, "setTimer", a => timers.Set(a.At(0), a.At(1).IsNumber() ? a.At(1).AsNumber() : 0, a.At(2).AsBoolean()));
        Add(engine, host, "clearTimer", a => Done(() =>
        {
            if (a.At(0).IsNumber())
            {
                timers.Clear((int)a.At(0).AsNumber());
            }
        }));
        Add(engine, host, "now", _ => timers.Now);

        Add(engine, host, "readText", a => File.ReadAllText(a.At(0).AsString()));
        Add(engine, host, "addModule", a => Done(() => modules.Add(a.At(0).AsString(), a.At(1).AsString())));
        Add(engine, host, "randomUuid", _ => Guid.NewGuid().ToString());
        Add(engine, host, "randomByte", _ => RandomNumberGenerator.GetInt32(256));
        return host;
    }

    private static void Add(Engine engine, JsObject host, string name, Func<JsValue[], JsValue> body) =>
        host.Set(name, new ClrFunction(engine, name, (_, arguments) => body(arguments)));

    private static JsValue Done(Action action)
    {
        action();
        return JsValue.Undefined;
    }
}
